// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Phos;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks
{
    // Creates the body a collider stands for and keeps it on the slot: a StaticBody3D for a surface, a
    // kinematic body for one that is carried about, or an Area3D for a shape that only answers queries.
    // CharacterController colliders belong to CharacterControllerHook, and a solid collider under a rigid
    // body is part of that body's shape, built by RigidBodyHook.
    [ImplementableHook(typeof(BoxCollider), typeof(CapsuleCollider), typeof(SphereCollider), typeof(CylinderCollider), typeof(Lumora.Core.Components.MeshCollider), typeof(ConeCollider), typeof(TriangleCollider), typeof(ConvexHullCollider))]
    public class PhysicsColliderHook : ComponentHook<Collider>
    {
        private Node3D _bodyNode = null!;
        private CollisionShape3D _collisionShape = null!;
        private Shape3D _shape = null!;
        private BodyKind _kind;
        private PhysicsMaterial _material = null!;
        private MeshInstance3D _debugMesh = null!;
        private static bool _showDebugColliders = false;

        private readonly Lumora.Godot.Helpers.ColliderShapeBake _bake = new();

        // Static: a surface where it was put. Moving: kinematic, so what rests on it is carried and woken when
        // it moves, and what it moves into is pushed. Query: an Area3D nothing collides with, found by casts.
        private enum BodyKind { None, Static, Moving, Query }

        // Last values actually pushed into the physics server, so a per-frame re-apply on a moving
        // collider stops writing the same numbers back.
        private bool _lastEnabled;
        private uint _lastLayer = uint.MaxValue;
        private uint _lastMask = uint.MaxValue;
        private Vector3 _lastBodyPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        private Quaternion _lastBodyRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        private int _lastQueryRules = -1;

        public override void Initialize()
        {
            base.Initialize();
            // Don't create body here - Type.Value may not be set yet
            // Body creation is deferred to ApplyChanges()
            NoteSolidPending(true);
        }

        // Pending until the first apply decides what this collider is, and after that for as long as a
        // character-blocking body has no shape in the space. A character with nothing under it waits on these.
        private void NoteSolidPending(bool pending)
            => (Owner?.World?.Hook as WorldHook)?.NoteSolidPending(this, pending);

        // Bodies made in one frame, across every collider. An arriving import brings hundreds of colliders
        // at once, and making them all in one frame handed the physics step hundreds of new bodies and
        // shapes to settle together: it ran out of jobs and waited, with the frame. The rest come back the
        // next frame. -xlinka
        private const int BodiesPerFrame = 24;
        private static ulong _bodyFrame = ulong.MaxValue;
        private static int _bodiesThisFrame;

        private static bool TakeBodyTurn()
        {
            ulong frame = global::Godot.Engine.GetProcessFrames();
            if (frame != _bodyFrame)
            {
                _bodyFrame = frame;
                _bodiesThisFrame = 0;
            }
            return _bodiesThisFrame++ < BodiesPerFrame;
        }

        // A character collider's shape lives in the character's own body, so an edit to it (an avatar fit,
        // the inspector) is passed to the controller that owns it. This apply also runs every frame the
        // user root moves, so only a real change of shape is passed on.
        private BoundingBox _ownerShapeBounds;
        private bool _ownerShapeKnown;

        private void NotifyOwnerOfShape()
        {
            var owner = Owner.ColliderOwner;
            if (owner == null)
                return;
            var bounds = Owner.GetLocalBounds();
            if (_ownerShapeKnown && bounds.Min == _ownerShapeBounds.Min && bounds.Max == _ownerShapeBounds.Max)
                return;
            _ownerShapeBounds = bounds;
            _ownerShapeKnown = true;
            owner.OnColliderShapeChanged(Owner);
        }

        public override void ApplyChanges()
        {
            // A solid collider with a rigid body at or above it is part of that body. The body on its own slot
            // is asked directly as well, for the first apply that can come before the body has gathered.
            bool carried = Owner.Body != null
                || (Owner.IsSolid && Owner.Slot.GetComponent<Lumora.Core.Components.RigidBody>() is { IsDestroyed: false });
            if (Owner.Type.Value == ColliderType.CharacterController)
                NotifyOwnerOfShape();
            if (Owner.Type.Value == ColliderType.CharacterController || carried)
            {
                // If we already created a body (Type changed after init), destroy it
                if (_bodyNode != null && GodotObject.IsInstanceValid(_bodyNode))
                {
                    LumoraLogger.Debug($"PhysicsColliderHook: Destroying body - carried={carried}, Type={Owner.Type.Value}");
                    DestroyBody(true);
                }
                NoteSolidPending(false);
                return;
            }

            var kind = DesiredKind();

            // Create body if needed (deferred from Initialize)
            if (_bodyNode == null || !GodotObject.IsInstanceValid(_bodyNode))
            {
                if (!TakeBodyTurn())
                {
                    Owner.MarkChangeDirty();
                    return;
                }
                LumoraLogger.Debug($"PhysicsColliderHook: Creating body for {Owner.GetType().Name} on '{Owner.Slot.SlotName.Value}'");
                CreateBody(kind);
                // BuildShape and UpdateTransform run below on every path; no need to do them twice.
            }
            else if (kind != _kind)
            {
                DestroyBody(true);
                CreateBody(kind);
            }

            BuildShape();

            UpdateTransform();

            // Enable/disable collision, scoped to this world's collision bit so bodies in different
            // worlds can never touch or answer each other's queries.
            // A moving collider runs this every frame (its world transform event queues the apply), and
            // every write below is a marshalled call that dirties the body in the physics server. Push
            // only what actually changed. -xlinka
            uint worldBit = WorldHook.GetCollisionBitFor(Owner?.World);

            // A disabled slot turns off everything under it, and this body cannot rely on that the way a
            // renderer can: it is parented under the WORLD ROOT, not under the slot, so Godot's visibility
            // never reaches it. Asking only the component's own Enabled leaves a collider on a switched-off
            // subtree fully solid - an imported avatar's swap meshes ship exactly like that, disabled at
            // their root, and you walk into geometry that is not on screen. -xlinka
            bool enabled = (Owner?.Enabled ?? false) && (Owner?.Slot?.IsActive ?? false);
            uint layer = enabled ? worldBit : 0u;

            // A collider that stops a walking body carries the world's character bit as well, and
            // that bit is the only thing the character's capsule is looking for. One that does not -
            // an imported model, a decorative prop - stays on the general bit, where lasers still hit
            // it and physics objects still rest on it, and a person walks straight through. A prop you
            // can pick up never stops people: it would shove the holder along with it. -xlinka
            if (enabled && _kind != BodyKind.Query && (Owner?.CharacterCollider.Value ?? false) && !IsPickedUpProp())
            {
                layer |= WorldHook.GetCharacterCollisionBitFor(Owner?.World);
            }
            // A query shape sits on the world bit, so casts find it, and masks nothing, so nothing pushes
            // against it.
            uint mask = _kind == BodyKind.Query ? 0u : (enabled ? worldBit : 0u);

            if (enabled != _lastEnabled)
            {
                _bodyNode!.Visible = enabled;
                _lastEnabled = enabled;
            }
            if (_bodyNode is CollisionObject3D collisionObject)
            {
                if (layer != _lastLayer)
                {
                    collisionObject.CollisionLayer = layer;
                    _lastLayer = layer;
                }
                if (mask != _lastMask)
                {
                    collisionObject.CollisionMask = mask;
                    _lastMask = mask;
                }
                PushQueryRules();
            }

            NoteSolidPending(enabled && _kind != BodyKind.Query
                && (layer & WorldHook.GetCharacterCollisionBitFor(Owner?.World)) != 0
                && _collisionShape?.Shape == null);
        }

        // NoCollision is met by every cast though nothing collides with it; a Trigger only by casts that ask
        // for triggers; IgnoreRaycasts by none.
        private void PushQueryRules()
        {
            bool queryArea = _kind == BodyKind.Query && Owner.Type.Value == ColliderType.NoCollision;
            bool ignoreRays = Owner.IgnoreRaycasts.Value;
            int rules = (queryArea ? 1 : 0) | (ignoreRays ? 2 : 0);
            if (rules == _lastQueryRules)
                return;
            _lastQueryRules = rules;
            (Owner?.World?.Hook as WorldHook)?.SetQueryRules(_registeredRid, queryArea, ignoreRays);
        }

        private BodyKind DesiredKind()
        {
            var type = Owner.Type.Value;
            if (type is ColliderType.Trigger or ColliderType.NoCollision)
                return BodyKind.Query;
            if (type == ColliderType.Active && Owner.Mass.Value > 0.0001f)
                return BodyKind.Moving;
            return IsPickedUpProp() ? BodyKind.Moving : BodyKind.Static;
        }

        // A grabbable or an image on the collider's own slot. As a sensor nothing could rest on it and casts that
        // skip triggers would pass through, so it is solid, on the general bit only, and kinematic, so what sits
        // on one rides with it when it is picked up. -xlinka
        private bool IsPickedUpProp()
        {
            var slot = Owner?.Slot;
            if (slot == null)
                return false;
            return slot.GetComponent<Grabbable>() != null || slot.GetComponent<ImageProvider>() != null;
        }

        private void CreateBody(BodyKind kind)
        {
            _kind = kind;
            switch (kind)
            {
                case BodyKind.Query:
                    _bodyNode = new Area3D
                    {
                        Name = "QueryCollider",
                        Monitoring = false,
                        Monitorable = true,
                    };
                    break;
                case BodyKind.Moving:
                    // Kinematic: nothing carries a simulated pose back to the object, so it follows the object
                    // and pushes what it moves into. -xlinka
                    _material = new PhysicsMaterial { Friction = 1f, Bounce = 0f };
                    _bodyNode = new RigidBody3D
                    {
                        Name = "MovingCollider",
                        PhysicsMaterialOverride = _material,
                        Mass = System.MathF.Max(Owner.Mass.Value, 0.001f),
                        FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
                        Freeze = true,
                    };
                    break;
                default:
                    _material = new PhysicsMaterial { Friction = 1f, Bounce = 0f };
                    _bodyNode = new StaticBody3D
                    {
                        Name = "StaticCollider",
                        PhysicsMaterialOverride = _material
                    };
                    break;
            }

            // A world in the background keeps its bodies in the physics space, so its scripts still meet them.
            if (_bodyNode is CollisionObject3D kept)
                kept.DisableMode = CollisionObject3D.DisableModeEnum.KeepActive;

            _collisionShape = new CollisionShape3D { Name = "Shape" };
            _bodyNode.AddChild(_collisionShape);

            if (Owner?.Slot != null)
            {
                SlotRefMeta.Tag(_bodyNode, Owner.Slot.ReferenceID);
            }

            // Parent under world root to avoid double transforms
            Node3D worldRoot = (Owner?.World?.GodotSceneRoot as Node3D)!;
            Node parentNode = (Node)worldRoot ?? attachedNode!;
            parentNode.AddChild(_bodyNode);

            if (_bodyNode is CollisionObject3D created)
            {
                _registeredRid = created.GetRid();
                _registeredSlot = Owner?.Slot;
                (Owner?.World?.Hook as WorldHook)?.RegisterBody(_registeredSlot, _registeredRid);
            }
        }

        // What was handed to the world's body registry, so the same pair comes back out on destroy.
        private Rid _registeredRid;
        private Lumora.Core.Slot? _registeredSlot;

        private void BuildShape()
        {
            if (_collisionShape == null || !GodotObject.IsInstanceValid(_collisionShape))
                return;

            var scale = Lumora.Godot.Helpers.ColliderShapeScaling.ScaleOf(Owner);
            var shape = _bake.Build(Owner, scale, simulated: false, out _);
            if (!ReferenceEquals(_shape, shape))
            {
                _shape = shape!;
                _collisionShape.Shape = shape;
            }
            if (shape == null)
                return;

            var offsetVector = Lumora.Godot.Helpers.ColliderShapeScaling.OffsetOf(Owner, scale);
            if (_collisionShape.Position != offsetVector) _collisionShape.Position = offsetVector;

            if (ShouldShowDebugForCollider())
            {
                UpdateDebugVisualization();
            }
            else
            {
                ClearDebugVisualization();
            }
        }

        private void UpdateDebugVisualization()
        {
            if (_debugMesh != null && GodotObject.IsInstanceValid(_debugMesh))
            {
                _debugMesh.QueueFree();
                _debugMesh = null!;
            }

            if (_bodyNode == null || !GodotObject.IsInstanceValid(_bodyNode))
                return;

            bool isImageCollider = IsImageCollider();

            _debugMesh = new MeshInstance3D();
            _debugMesh.Name = "DebugCollider";

            var material = new StandardMaterial3D();
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.AlbedoColor = new Color(0.2f, 0.5f, 1.0f, 0.8f); // Blue
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

            switch (Owner)
            {
                case BoxCollider box:
                    var boxSize = ((BoxShape3D)_shape).Size;
                    if (isImageCollider)
                    {
                        _debugMesh.Mesh = BuildBoxWireMesh(boxSize);
                        material.AlbedoColor = new Color(0.75f, 0.2f, 1.0f, 1.0f);
                    }
                    else
                    {
                        var boxMesh = new BoxMesh();
                        boxMesh.Size = boxSize;
                        _debugMesh.Mesh = boxMesh;
                        material.AlbedoColor = new Color(0.2f, 0.5f, 1.0f, 0.3f);
                    }
                    break;

                case CapsuleCollider capsule:
                    var capsuleMesh = new CapsuleMesh();
                    capsuleMesh.Radius = ((CapsuleShape3D)_shape).Radius;
                    capsuleMesh.Height = ((CapsuleShape3D)_shape).Height;
                    _debugMesh.Mesh = capsuleMesh;
                    material.AlbedoColor = new Color(0.2f, 1.0f, 0.5f, 0.3f); // Green for capsule
                    break;

                case SphereCollider sphere:
                    var sphereMesh = new SphereMesh();
                    sphereMesh.Radius = ((SphereShape3D)_shape).Radius;
                    sphereMesh.Height = sphereMesh.Radius * 2f;
                    _debugMesh.Mesh = sphereMesh;
                    material.AlbedoColor = new Color(1.0f, 0.5f, 0.2f, 0.3f); // Orange for sphere
                    break;

                case CylinderCollider cylinder:
                    var cylinderMesh = new CylinderMesh();
                    cylinderMesh.TopRadius = ((CylinderShape3D)_shape).Radius;
                    cylinderMesh.BottomRadius = ((CylinderShape3D)_shape).Radius;
                    cylinderMesh.Height = ((CylinderShape3D)_shape).Height;
                    _debugMesh.Mesh = cylinderMesh;
                    material.AlbedoColor = new Color(0.8f, 0.2f, 0.8f, 0.3f); // Purple for cylinder
                    break;
            }

            _debugMesh.MaterialOverride = material;

            _debugMesh.Position = _collisionShape.Position;

            _bodyNode.AddChild(_debugMesh);
        }

        private void ClearDebugVisualization()
        {
            if (_debugMesh != null && GodotObject.IsInstanceValid(_debugMesh))
            {
                _debugMesh.QueueFree();
            }
            _debugMesh = null!;
        }

        private bool ShouldShowDebugForCollider()
        {
            return _showDebugColliders;
        }

        private bool IsImageCollider()
        {
            if (Owner?.Slot == null)
                return false;

            return Owner.Slot.GetComponent<ImageProvider>() != null;
        }

        private static ArrayMesh? BuildBoxWireMesh(Vector3 size)
        {
            float hx = size.X * 0.5f;
            float hy = size.Y * 0.5f;
            float hz = size.Z * 0.5f;

            var corners = new[]
            {
                new Vector3(-hx, -hy, -hz),
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz),
                new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, hz),
                new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(-hx, hy, hz)
            };

            var vertices = new System.Collections.Generic.List<Vector3>();
            var indices = new System.Collections.Generic.List<int>();

            AddLine(vertices, indices, corners[0], corners[1]);
            AddLine(vertices, indices, corners[1], corners[2]);
            AddLine(vertices, indices, corners[2], corners[3]);
            AddLine(vertices, indices, corners[3], corners[0]);

            AddLine(vertices, indices, corners[4], corners[5]);
            AddLine(vertices, indices, corners[5], corners[6]);
            AddLine(vertices, indices, corners[6], corners[7]);
            AddLine(vertices, indices, corners[7], corners[4]);

            AddLine(vertices, indices, corners[0], corners[4]);
            AddLine(vertices, indices, corners[1], corners[5]);
            AddLine(vertices, indices, corners[2], corners[6]);
            AddLine(vertices, indices, corners[3], corners[7]);

            return BuildLineMesh(vertices, indices);
        }

        private static ArrayMesh? BuildLineMesh(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> indices)
        {
            if (vertices.Count == 0) return null;
            var mesh = new ArrayMesh();
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            return mesh;
        }

        private static void AddLine(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> indices, Vector3 from, Vector3 to)
        {
            int start = vertices.Count;
            vertices.Add(from);
            vertices.Add(to);
            indices.Add(start);
            indices.Add(start + 1);
        }

        private void UpdateTransform()
        {
            if (_bodyNode == null || !GodotObject.IsInstanceValid(_bodyNode))
                return;

            var slotNode = slotHook?.GeneratedNode3D;
            if (slotNode == null)
                return;

            // Only copy position and rotation - NOT scale. The shape already carries the real
            // dimensions, so scaling here would double-apply; scale stays at (1,1,1).
            //
            // Guarded on the value. A collider is re-applied for plenty of reasons other than moving (a
            // sync field, an enable, a shape edit), and a transform write on a static body re-inserts it
            // into the broadphase and wakes whatever was resting on it. Same pose in, nothing out.
            // -xlinka
            // From the slot itself, never its node: the node takes its pose when the slot hook flushes, which
            // for a slot made this frame is after us, and a body placed from it sat at the node's old pose
            // until the slot happened to move again. Reading the slot also keeps its world transform computed,
            // which is what lets the next move of anything above it reach this collider. -xlinka
            bool inTree = slotNode.IsInsideTree();
            var globalPos = Owner.Slot.GlobalPosition;
            var globalRot = Owner.Slot.GlobalRotation;
            var position = new Vector3(globalPos.x, globalPos.y, globalPos.z);
            var rotation = new Quaternion(globalRot.x, globalRot.y, globalRot.z, globalRot.w);

            if (position == _lastBodyPosition && rotation == _lastBodyRotation)
                return;

            _lastBodyPosition = position;
            _lastBodyRotation = rotation;

            if (inTree)
            {
                _bodyNode.GlobalPosition = position;
                _bodyNode.GlobalBasis = new Basis(rotation);
            }
            else
            {
                _bodyNode.Position = position;
                _bodyNode.Quaternion = rotation;
            }
        }

        public override void Destroy(bool destroyingWorld)
        {
            NoteSolidPending(false);
            DestroyBody(!destroyingWorld);
            base.Destroy(destroyingWorld);
        }

        private void DestroyBody(bool queueFree)
        {
            if (_registeredRid.IsValid)
            {
                (Owner?.World?.Hook as WorldHook)?.ClearQueryRules(_registeredRid);
                (Owner?.World?.Hook as WorldHook)?.UnregisterBody(_registeredSlot, _registeredRid);
                _registeredRid = default;
                _registeredSlot = null;
            }
            // A body built after this one starts from Godot's defaults (layer 1, mask 1, visible), so
            // the caches must not claim those values were already pushed.
            _lastEnabled = false;
            _lastLayer = uint.MaxValue;
            _lastMask = uint.MaxValue;
            _lastBodyPosition = new Vector3(float.NaN, float.NaN, float.NaN);
            _lastBodyRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
            _lastQueryRules = -1;
            _kind = BodyKind.None;
            _bake.Clear();

            if (_debugMesh != null && GodotObject.IsInstanceValid(_debugMesh) && queueFree)
            {
                _debugMesh.QueueFree();
            }

            if (_collisionShape != null && GodotObject.IsInstanceValid(_collisionShape) && queueFree)
            {
                _collisionShape.QueueFree();
            }

            if (_bodyNode != null && GodotObject.IsInstanceValid(_bodyNode) && queueFree)
            {
                _bodyNode.QueueFree();
            }

            _debugMesh = null!;
            _collisionShape = null!;
            _bodyNode = null!;
            _shape = null!;
        }
    }
}
