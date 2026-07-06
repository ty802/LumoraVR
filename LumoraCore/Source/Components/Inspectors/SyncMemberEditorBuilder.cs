// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Builds the editor row for any sync member: label on the left, a type-appropriate editor on the
/// right. Dispatch is member kind -> value type, with unknown structs decomposed field-by-field into
/// path-addressed leaf editors (float3 becomes three floats, and so on).
/// </summary>
public static class SyncMemberEditorBuilder
{
    private const float LabelFraction = 0.34f;
    private const int MaxStructDepth = 2;

    /// <summary>One member row into the container: label left, type-appropriate editor right.</summary>
    public static void Build(ISyncMember member, string name, FieldInfo? fieldInfo, Slot container, Slot themeContext)
    {
        InspectorUI.FixedRow(container, name, InspectorUI.RowHeight, out var ui, themeContext);

        bool driven = IsDriven(member);

        ui.PushStyle();
        ui.MinWidth(150f);
        ui.PreferredWidth(220f);
        ui.FlexibleWidth(LabelFraction);
        var label = ui.Text(name, InspectorUI.FontSize, driven ? InspectorUI.DrivenColor : InspectorUI.MutedColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var editorArea = ui.Next("Editor");
        // Single-child containers need a layout or the child sits as a default 100x100 chunk.
        var areaLayout = editorArea.AttachComponent<Helio.UI.Layout.HorizontalLayout>();
        areaLayout.ForceExpandHeight.Value = true;
        ui.NestInto(editorArea);
        BuildEditor(member, fieldInfo, ui, editorArea);
        ui.NestOut();
        ui.PopStyle();
    }

    private static bool IsDriven(ISyncMember member)
        => member is SyncElement element && !element.IsDrivable; // conservative: marked non-drivable reads muted too

    private static bool IsTextureRef(ISyncMember member)
    {
        var type = member.GetType();
        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Lumora.Core.Assets.AssetRef<>)
            && type.GetGenericArguments()[0] == typeof(Lumora.Core.Assets.TextureAsset);
    }

    private static void BuildEditor(ISyncMember member, FieldInfo? fieldInfo, UIBuilder ui, Slot editorSlot)
    {
        switch (member)
        {
            case ISyncRef when member is IField textureField && IsTextureRef(member):
                Attach<TextureRefMemberEditor>(editorSlot).Setup(textureField, "", ui);
                return;

            case ISyncRef syncRef when member is IField refField:
                Attach<RefMemberEditor>(editorSlot).Setup(refField, "", ui);
                return;

            case IField field:
                BuildFieldEditor(field, field.ValueType, "", fieldInfo, ui, editorSlot, 0);
                return;

            case ISyncList list:
                ui.Text($"(list, {list.Count} items)", InspectorUI.FontSize, InspectorUI.MutedColor);
                return;

            default:
                ui.Text($"({member.GetType().Name})", InspectorUI.FontSize, InspectorUI.MutedColor);
                return;
        }
    }

    private static void BuildFieldEditor(IField field, Type type, string path, FieldInfo? fieldInfo, UIBuilder ui, Slot editorSlot, int depth)
    {
        if (type == typeof(bool))
        {
            Attach<BooleanMemberEditor>(editorSlot).Setup(field, path, ui);
            return;
        }
        if (type.IsEnum)
        {
            Attach<EnumMemberEditor>(editorSlot).Setup(field, path, ui);
            return;
        }
        if (type == typeof(color) || type == typeof(colorHDR))
        {
            Attach<ColorMemberEditor>(editorSlot).Setup(field, path, ui);
            return;
        }
        if (type == typeof(floatQ))
        {
            Attach<QuaternionMemberEditor>(editorSlot).Setup(field, path, ui);
            return;
        }
        if (IsNumeric(type) && fieldInfo?.GetCustomAttribute<RangeAttribute>() is { } range)
        {
            var slider = Attach<SliderMemberEditor>(editorSlot);
            slider.Min.Value = range.Min;
            slider.Max.Value = range.Max;
            slider.WholeNumbers.Value = IsInteger(type);
            slider.Setup(field, path, ui);
            return;
        }
        if (IsTextEditable(type))
        {
            Attach<PrimitiveMemberEditor>(editorSlot).Setup(field, path, ui);
            return;
        }

        // Compound struct: one labeled leaf row per instance field, addressed by dotted path.
        if (type.IsValueType && depth < MaxStructDepth)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fields.Length > 0 && fields.Length <= 6)
            {
                ui.HorizontalLayout(4f);
                foreach (var structField in fields)
                {
                    ui.PushStyle();
                    ui.FlexibleWidth(1f);
                    var leafSlot = ui.Next(structField.Name);
                    ui.NestInto(leafSlot);
                    string leafPath = string.IsNullOrEmpty(path) ? structField.Name : path + "." + structField.Name;
                    BuildFieldEditor(field, structField.FieldType, leafPath, null, ui, leafSlot, depth + 1);
                    ui.NestOut();
                    ui.PopStyle();
                }
                ui.NestOut();
                return;
            }
        }

        ui.Text($"({NiceTypeName(type)})", InspectorUI.FontSize, InspectorUI.MutedColor);
    }

    private static T Attach<T>(Slot slot) where T : Component, new() => slot.AttachComponent<T>();

    private static bool IsNumeric(Type type)
        => type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(byte) || type == typeof(sbyte) || type == typeof(decimal);

    private static bool IsInteger(Type type)
        => type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);

    private static bool IsTextEditable(Type type)
        => type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Uri);

    public static string NiceTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        var args = type.GetGenericArguments();
        var argNames = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            argNames[i] = NiceTypeName(args[i]);
        return $"{name}<{string.Join(", ", argNames)}>";
    }
}

/// <summary>Optional hook: a component builds its own inspector body instead of the reflected rows.</summary>
public interface ICustomInspector
{
    void BuildInspectorUI(UIBuilder ui);
}

/// <summary>Reflected member rows for a worker (skips [HideInInspector]).</summary>
public static class WorkerInspectorBuilder
{
    public static void BuildMemberRows(Worker worker, Slot container, Slot themeContext)
    {
        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            var fieldInfo = worker.GetSyncMemberFieldInfo(i);
            if (fieldInfo?.GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;
            var member = worker.GetSyncMember(i);
            if (member == null)
                continue;
            SyncMemberEditorBuilder.Build(member, worker.GetSyncMemberName(i), fieldInfo, container, themeContext);
        }
    }
}

/// <summary>
/// Press relay carrying a synced string argument to an IInspectorActionHandler - the duplication-
/// and network-safe replacement for closure button actions in inspector lists.
/// </summary>
public class InspectorButtonRelay : Component
{
    public readonly Sync<string> Argument;
    public readonly SyncRef<Component> Handler;

    public InspectorButtonRelay()
    {
        Argument = new Sync<string>(this, "");
        Handler = new SyncRef<Component>(this);
    }

    [SyncMethod]
    public void OnPressed(Button button, UIInteractionContext context)
    {
        (Handler.Target as IInspectorActionHandler)?.HandleInspectorAction(Argument.Value ?? "");
    }
}
