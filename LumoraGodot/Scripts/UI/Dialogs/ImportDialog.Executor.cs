// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;
using LumoraMeshes = Lumora.Core.Components.Meshes;

namespace Lumora.Godot.UI;

/// <summary>
/// Partial class: per-type import execution.
/// </summary>
public partial class ImportDialog
{
	private async Task<bool> PerformImport(ImportType type, string filePath)
	{
		GD.Print($"ImportDialog: Performing {type} import of '{filePath}'");
		_isImporting = true;
		SetImportInProgress(true, $"Importing {type}...", 0f);
		SetSubtitle("Import in progress...");

		var progress = new Progress<(float progress, string status)>(
			update => ReportProgress(update.progress, update.status));

		try
		{
			switch (type)
			{
				case ImportType.ImageTexture:
					ReportProgress(0.1f, "Importing image...");
					await ImportImage(filePath);
					ReportProgress(1.0f, "Image import complete");
					break;

				case ImportType.Model3D:
					await ImportModel(filePath, isAvatar: false, progress);
					break;

				case ImportType.Avatar:
					await ImportModel(filePath, isAvatar: true, progress);
					break;

				case ImportType.Shader:
					ReportProgress(0.1f, "Importing custom shader...");
					await ImportShader(filePath);
					ReportProgress(1.0f, "Shader workbench ready");
					break;

				case ImportType.RawFile:
					ReportProgress(0.1f, "Importing raw file...");
					await ImportRawFile(filePath);
					ReportProgress(1.0f, "Raw file import complete");
					break;
			}

			SetSubtitle("Import completed");
			if (type == ImportType.Avatar)
				SetCompletedStatus("Avatar imported as draft. Open Creator to finish it.");
			else
				SetCompletedStatus("Import queued. Model may take a moment to appear in-world.");

			GD.Print("ImportDialog: Import completed successfully");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportDialog: Import failed: {ex.Message}");
			SetSubtitle($"Import failed: {ex.Message}");
			SetCompletedStatus($"Import failed: {ex.Message}", success: false);
			return false;
		}
		finally
		{
			_isImporting = false;
		}
	}

	private async Task ImportImage(string filePath)
	{
		string localUri = null!;
		if (_localDB != null)
			localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);

		var imageSlot = _targetSlot.AddSlot(Path.GetFileNameWithoutExtension(filePath));
		PositionImportedSlot(imageSlot);

		// build the visual quad. without this the slot is invisible. - xlinka
		var quadMesh = imageSlot.AttachComponent<LumoraMeshes.QuadMesh>();
		quadMesh.Size.Value = new float2(1.0f, 1.0f);
		quadMesh.DualSided.Value = true;

		var meshRenderer = imageSlot.AttachComponent<MeshRenderer>();
		meshRenderer.Mesh.Target = quadMesh;

		// ImageProvider must exist before BoxCollider so PhysicsColliderHook detects it as an image and builds a sensor instead of a wall - xlinka
		var imageProvider = imageSlot.AttachComponent<ImageProvider>();
		imageProvider.URL.Value = new Uri(localUri ?? filePath);

		var collider = imageSlot.AttachComponent<BoxCollider>();
		collider.Size.Value = new float3(1f, 1f, 0.02f);

		imageSlot.AttachComponent<Grabbable>();

		var sizeDriver = imageSlot.AttachComponent<TextureSizeDriver>();
		sizeDriver.Source.Target = imageProvider;
		sizeDriver.Target.Target = quadMesh;
		sizeDriver.ColliderTarget.Target = collider;

		var material = imageSlot.AttachComponent<UnlitMaterial>();
		material.Texture.Target = imageProvider;
		// flipped X uv. PNGs come in mirrored otherwise. - xlinka
		material.TextureScale.Value = new float2(-1f, 1f);
		material.TextureOffset.Value = new float2(1f, 0f);
		material.BlendMode.Value = BlendMode.Transparent;
		meshRenderer.Material.Target = material;

		GD.Print($"ImportDialog: Image imported to {localUri ?? filePath}");
	}

	private async Task ImportModel(string filePath, bool isAvatar, IProgress<(float progress, string status)> progress)
	{
		ModelImportResult result;

		if (isAvatar)
		{
			progress?.Report((0.05f, "Preparing avatar import..."));
			result = await ModelImporter.ImportAvatarAsync(filePath, _targetSlot, _localDB, progress);
		}
		else
		{
			progress?.Report((0.05f, "Preparing model import..."));
			result = await ModelImporter.ImportModelAsync(filePath, _targetSlot, null!, _localDB, progress);
		}

		if (!result.Success)
		{
			GD.PrintErr($"ImportDialog: Model import failed: {result.ErrorMessage}");
			progress?.Report((0f, $"Import failed: {result.ErrorMessage}"));
			throw new InvalidOperationException(result.ErrorMessage);
		}

		PositionImportedSlot(result.RootSlot);

		// Make the import grabbable so you can pick it up, reposition, and scale it - matches the ref (which makes
		// every import grabbable) and our own image/shader imports. Without this the model just sits there with
		// nothing to grab. Attached to the root so a grab moves the whole model. -xlinka
		if (result.RootSlot != null && !result.RootSlot.IsDestroyed)
		{
			var grab = result.RootSlot.AttachComponent<Grabbable>();
			grab.AllowGrab.Value = true;
			grab.Scalable.Value = true;
		}

		// Register an undo point so a freshly imported model is one Ctrl-Z away, like the ref (CreateSpawnUndoPoint).
		RecordImportUndo(result.RootSlot, isAvatar ? "Import Avatar" : "Import Model");

		if (isAvatar)
		{
			ResetAvatarCreatorState();
			_lastImportedAvatarSlot = result.RootSlot;
			UpdateAvatarSetupButton();
		}
		else
		{
			_lastImportedAvatarSlot = null!;
			UpdateAvatarSetupButton();
		}

		progress?.Report((0.90f, isAvatar ? "Avatar imported as draft..." : "Finalizing model..."));
		GD.Print($"ImportDialog: Model imported successfully to slot '{result.RootSlot?.SlotName.Value}'");
		progress?.Report((1.0f, isAvatar ? "Avatar imported as draft" : "Model import complete"));
	}

	private void PositionImportedSlot(Slot importedSlot)
	{
		if (importedSlot == null || importedSlot.IsDestroyed)
			return;

		if (_hasImportSpawnPosition)
		{
			importedSlot.GlobalPosition = _importSpawnPosition;
			return;
		}

		// Spawn in front of the local user's view, facing them - the ref imports under LocalUserSpace positioned
		// in front of the user; we'd otherwise dump it at the world origin (which is why it appeared off to the
		// side instead of ahead). Flatten the look direction to horizontal so the model stands upright. -xlinka
		var head = _targetSlot?.World?.LocalUser?.Root?.HeadSlot;
		if (head != null && !head.IsDestroyed)
		{
			var hp = head.GlobalPosition;
			var f = head.Forward;
			var fwd = new float3(f.x, 0f, f.z);
			fwd = fwd.LengthSquared < 1e-6f ? new float3(0f, 0f, 1f) : fwd.Normalized;
			// ~1.5 m ahead, dropped ~1 m below eye height so a standing avatar sits centered in view.
			importedSlot.GlobalPosition = new float3(hp.x + fwd.x * 1.5f, hp.y - 1.0f, hp.z + fwd.z * 1.5f);
			// Face the user with a PURE YAW (point +Z back at them). floatQ.LookRotation is broken for facing - it
			// returns the inverse rotation and sends content edge-on (that's the sideways/horizontal pose); the
			// working FaceLocalUser uses atan2 instead, so we do the same. -xlinka
			var toUser = new float3(-fwd.x, 0f, -fwd.z); // spawn is ahead of the user, so back-toward-user is -fwd
			importedSlot.GlobalRotation = floatQ.AxisAngle(float3.Up, System.MathF.Atan2(toUser.x, toUser.z));
			return;
		}

		if (_targetSlot != null && !_targetSlot.IsDestroyed && !_targetSlot.IsRootSlot)
			importedSlot.GlobalPosition = _targetSlot.GlobalPosition;
	}

	// Register a freshly imported root with the local user's undo history so it's one Ctrl-Z away (the ref's
	// CreateSpawnUndoPoint). No-op if no UndoManager is reachable - harmless, just no undo entry. -xlinka
	private void RecordImportUndo(Slot root, string description)
	{
		if (root == null || root.IsDestroyed)
			return;

		var world = root.World;
		var undo = root.ActiveUserRoot?.Slot?.GetComponentInChildren<UndoManager>()
		           ?? world?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();
		if (undo == null)
			return;

		var batch = SlotExistenceUndoBatch.Created(world, new[] { root }, description);
		if (batch != null)
			undo.Record(batch);
	}

	private async Task ImportRawFile(string filePath)
	{
		if (_localDB != null)
		{
			var localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
			GD.Print($"ImportDialog: Raw file imported to {localUri}");
		}
	}

	// mirrors ClipboardImporter.ImportShader so paste and file-pick produce the same setup - xlinka
	private async Task ImportShader(string filePath)
	{
		string localUri = null!;
		if (_localDB != null)
			localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);

		var shaderName = Path.GetFileNameWithoutExtension(filePath);
		if (string.IsNullOrWhiteSpace(shaderName))
			shaderName = "CustomShader";

		var rootSlot = _targetSlot.AddSlot($"{shaderName}_ShaderWorkbench");
		PositionImportedSlot(rootSlot);

		var sourceSlot = rootSlot.AddSlot("ShaderSource");
		var shaderProvider = sourceSlot.AttachComponent<ShaderSourceProvider>();
		shaderProvider.URL.Value = new Uri(localUri ?? filePath);

		var materialSlot = rootSlot.AddSlot("Material");
		var material = materialSlot.AttachComponent<CustomShaderMaterial>();
		material.Shader.Target = shaderProvider;

		var sphereSlot = rootSlot.AddSlot("PreviewSphere");
		sphereSlot.LocalPosition.Value = new float3(-0.35f, 0f, 0f);

		var sphereMesh = sphereSlot.AttachComponent<LumoraMeshes.SphereMesh>();
		sphereMesh.Radius.Value = 0.3f;
		sphereMesh.Segments.Value = 32;
		sphereMesh.Rings.Value = 16;

		var meshRenderer = sphereSlot.AttachComponent<MeshRenderer>();
		meshRenderer.Mesh.Target = sphereMesh;
		meshRenderer.Material.Target = material;

		// Grabbable + collider so users can pick the orb up. Grabbable attached first so the
		// SphereCollider hook sees it and builds a sensor instead of a wall - xlinka
		sphereSlot.AttachComponent<Grabbable>();
		var sphereCollider = sphereSlot.AttachComponent<SphereCollider>();
		sphereCollider.Radius.Value = 0.3f;

		GD.Print($"ImportDialog: Shader workbench built for {localUri ?? filePath}");
	}
}
