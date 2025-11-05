using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Aquamarine.Source.Core;

namespace Aquamarine.Source.Inspector;

/// <summary>
/// Main inspector system for viewing and editing Slots and Components in-world.
/// </summary>
public partial class InspectorSystem : Control
{
	private Slot _selectedSlot;
	private Component _selectedComponent;
	private VBoxContainer _propertyContainer;
	private ScrollContainer _scrollContainer;
	private readonly Dictionary<Type, Func<Control>> _attributeFactories = new();

	[Signal]
	public delegate void SelectionChangedEventHandler();

	public override void _Ready()
	{
		SetupUI();
		RegisterAttributeFactories();
	}

	private void SetupUI()
	{
		// Create main layout
		var vbox = new VBoxContainer();
		AddChild(vbox);

		// Header
		var header = new Label();
		header.Text = "Inspector";
		header.AddThemeColorOverride("font_color", Colors.White);
		vbox.AddChild(header);

		// Scroll container for properties
		_scrollContainer = new ScrollContainer();
		_scrollContainer.CustomMinimumSize = new Vector2(300, 400);
		vbox.AddChild(_scrollContainer);

		// Property container
		_propertyContainer = new VBoxContainer();
		_scrollContainer.AddChild(_propertyContainer);
	}

	/// <summary>
	/// Register all attribute type factories for property display.
	/// </summary>
	private void RegisterAttributeFactories()
	{
		// Basic types
		_attributeFactories[typeof(bool)] = CreateBoolAttribute;
		_attributeFactories[typeof(int)] = CreateNumberAttribute;
		_attributeFactories[typeof(float)] = CreateNumberAttribute;
		_attributeFactories[typeof(double)] = CreateNumberAttribute;
		_attributeFactories[typeof(string)] = CreateStringAttribute;

		// Godot types
		_attributeFactories[typeof(Vector2)] = CreateVector2Attribute;
		_attributeFactories[typeof(Vector3)] = CreateVector3Attribute;
		_attributeFactories[typeof(Color)] = CreateColorAttribute;
		_attributeFactories[typeof(Quaternion)] = CreateQuaternionAttribute;
	}

	/// <summary>
	/// Select a Slot for inspection.
	/// </summary>
	public void SelectSlot(Slot slot)
	{
		_selectedSlot = slot;
		_selectedComponent = null;
		RefreshInspector();
		EmitSignal(SignalName.SelectionChanged);
	}

	/// <summary>
	/// Select a Component for inspection.
	/// </summary>
	public void SelectComponent(Component component)
	{
		_selectedComponent = component;
		_selectedSlot = component?.Slot;
		RefreshInspector();
		EmitSignal(SignalName.SelectionChanged);
	}

	/// <summary>
	/// Refresh the inspector to show current selection's properties.
	/// </summary>
	private void RefreshInspector()
	{
		// Clear existing properties
		foreach (var child in _propertyContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_selectedComponent != null)
		{
			InspectComponent(_selectedComponent);
		}
		else if (_selectedSlot != null)
		{
			InspectSlot(_selectedSlot);
		}
	}

	/// <summary>
	/// Display Slot properties in the inspector.
	/// </summary>
	private void InspectSlot(Slot slot)
	{
		AddHeader($"Slot: {slot.SlotName.Value}");

		// Slot name
		AddSyncProperty("Name", slot.SlotName);

		// Transform properties
		AddSyncProperty("Position", slot.LocalPosition);
		AddSyncProperty("Rotation", slot.LocalRotation);
		AddSyncProperty("Scale", slot.LocalScale);

		// Slot flags
		AddSyncProperty("Active", slot.ActiveSelf);
		AddSyncProperty("Persistent", slot.Persistent);
		AddSyncProperty("Tag", slot.Tag);

		// Component list
		AddHeader("Components");
		foreach (var component in slot.Components)
		{
			var button = new Button();
			button.Text = component.ComponentName;
			button.Pressed += () => SelectComponent(component);
			_propertyContainer.AddChild(button);
		}

		// Add component button
		var addComponentBtn = new Button();
		addComponentBtn.Text = "+ Add Component";
		addComponentBtn.Pressed += () => ShowAddComponentMenu(slot);
		_propertyContainer.AddChild(addComponentBtn);

		// Children list
		AddHeader($"Children ({slot.Children.Count})");
		foreach (var child in slot.Children)
		{
			var childBtn = new Button();
			childBtn.Text = child.SlotName.Value;
			childBtn.Pressed += () => SelectSlot(child);
			_propertyContainer.AddChild(childBtn);
		}

		// Add child slot button
		var addSlotBtn = new Button();
		addSlotBtn.Text = "+ Add Child Slot";
		addSlotBtn.Pressed += () => slot.AddSlot("New Slot");
		_propertyContainer.AddChild(addSlotBtn);
	}

	/// <summary>
	/// Display Component properties in the inspector using reflection.
	/// </summary>
	private void InspectComponent(Component component)
	{
		AddHeader($"Component: {component.ComponentName}");

		// Add enabled toggle
		AddSyncProperty("Enabled", component.Enabled);

		// Use reflection to find all Sync<T> properties
		var type = component.GetType();
		var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

		foreach (var prop in properties)
		{
			if (prop.PropertyType.IsGenericType &&
				prop.PropertyType.GetGenericTypeDefinition() == typeof(Sync<>))
			{
				var syncValue = prop.GetValue(component);
				if (syncValue != null)
				{
					AddSyncProperty(prop.Name, syncValue);
				}
			}
		}

		// Back to slot button
		var backBtn = new Button();
		backBtn.Text = "← Back to Slot";
		backBtn.Pressed += () => SelectSlot(component.Slot);
		_propertyContainer.AddChild(backBtn);

		// Delete component button
		var deleteBtn = new Button();
		deleteBtn.Text = "Delete Component";
		deleteBtn.Pressed += () => {
			component.Destroy();
			SelectSlot(component.Slot);
		};
		_propertyContainer.AddChild(deleteBtn);
	}

	private void AddHeader(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", Colors.Yellow);
		_propertyContainer.AddChild(label);
	}

	/// <summary>
	/// Add a property editor for a Sync<T> field.
	/// Uses type-based factory pattern to create appropriate UI.
	/// </summary>
	private void AddSyncProperty(string name, object syncField)
	{
		var container = new HBoxContainer();
		_propertyContainer.AddChild(container);

		// Property label
		var label = new Label();
		label.Text = name + ":";
		label.CustomMinimumSize = new Vector2(100, 0);
		container.AddChild(label);

		// Get the value type
		var syncType = syncField.GetType();
		var valueType = syncType.GetGenericArguments()[0];

		// Create appropriate editor using factory
		Control editor = null;
		if (_attributeFactories.TryGetValue(valueType, out var factory))
		{
			editor = factory();
			SetupPropertyBinding(editor, syncField, valueType);
		}
		else
		{
			// Fallback to simple label for unknown types
			editor = new Label();
			((Label)editor).Text = syncField.ToString();
		}

		container.AddChild(editor);
	}

	/// <summary>
	/// Setup two-way binding between UI control and Sync<T> field.
	/// </summary>
	private void SetupPropertyBinding(Control editor, object syncField, Type valueType)
	{
		var valueProperty = syncField.GetType().GetProperty("Value");

		if (editor is LineEdit lineEdit)
		{
			// Initial value
			lineEdit.Text = valueProperty.GetValue(syncField)?.ToString() ?? "";

			// Update sync when text changes (only when not focused)
			lineEdit.TextSubmitted += (newText) => {
				var converted = Convert.ChangeType(newText, valueType);
				valueProperty.SetValue(syncField, converted);
			};

			// Subscribe to changes from sync
			var onChangedField = syncField.GetType().GetField("OnChanged");
			if (onChangedField != null)
			{
				var delegateType = onChangedField.FieldType;
				var updateMethod = GetType().GetMethod(nameof(UpdateLineEdit), BindingFlags.NonPublic | BindingFlags.Instance);
				var boundUpdateMethod = updateMethod.MakeGenericMethod(valueType);

				// Create delegate
				var handler = Delegate.CreateDelegate(delegateType,
					this,
					boundUpdateMethod);

				// Add handler
				var currentDelegate = onChangedField.GetValue(syncField);
				var combined = Delegate.Combine(currentDelegate as Delegate, handler);
				onChangedField.SetValue(syncField, combined);

			}
		}
		else if (editor is CheckBox checkBox)
		{
			checkBox.ButtonPressed = (bool)valueProperty.GetValue(syncField);

			checkBox.Toggled += (pressed) => {
				valueProperty.SetValue(syncField, pressed);
			};
		}
		else if (editor is SpinBox spinBox)
		{
			var value = valueProperty.GetValue(syncField);
			spinBox.Value = Convert.ToDouble(value);

			spinBox.ValueChanged += (newValue) => {
				var converted = Convert.ChangeType(newValue, valueType);
				valueProperty.SetValue(syncField, converted);
			};
		}
	}

	private void UpdateLineEdit<T>(T value)
	{
		// This is called via reflection when sync value changes
		// Implementation would check focus and update text
	}

	#region Attribute Factory Methods

	private Control CreateBoolAttribute()
	{
		return new CheckBox();
	}

	private Control CreateNumberAttribute()
	{
		var spinBox = new SpinBox();
		spinBox.MinValue = double.MinValue;
		spinBox.MaxValue = double.MaxValue;
		spinBox.Step = 0.01;
		spinBox.CustomMinimumSize = new Vector2(150, 0);
		return spinBox;
	}

	private Control CreateStringAttribute()
	{
		var lineEdit = new LineEdit();
		lineEdit.CustomMinimumSize = new Vector2(150, 0);
		return lineEdit;
	}

	private Control CreateVector2Attribute()
	{
		var container = new HBoxContainer();

		var xSpin = new SpinBox { Step = 0.01, MinValue = double.MinValue, MaxValue = double.MaxValue };
		var ySpin = new SpinBox { Step = 0.01, MinValue = double.MinValue, MaxValue = double.MaxValue };

		container.AddChild(new Label { Text = "X:" });
		container.AddChild(xSpin);
		container.AddChild(new Label { Text = "Y:" });
		container.AddChild(ySpin);

		return container;
	}

	private Control CreateVector3Attribute()
	{
		var container = new HBoxContainer();

		var xSpin = new SpinBox { Step = 0.01, MinValue = double.MinValue, MaxValue = double.MaxValue };
		var ySpin = new SpinBox { Step = 0.01, MinValue = double.MinValue, MaxValue = double.MaxValue };
		var zSpin = new SpinBox { Step = 0.01, MinValue = double.MinValue, MaxValue = double.MaxValue };

		container.AddChild(new Label { Text = "X:" });
		container.AddChild(xSpin);
		container.AddChild(new Label { Text = "Y:" });
		container.AddChild(ySpin);
		container.AddChild(new Label { Text = "Z:" });
		container.AddChild(zSpin);

		return container;
	}

	private Control CreateQuaternionAttribute()
	{
		// Display as euler angles for user-friendliness
		return CreateVector3Attribute();
	}

	private Control CreateColorAttribute()
	{
		return new ColorPickerButton();
	}

	#endregion

	private void ShowAddComponentMenu(Slot slot)
	{
		// TODO: Implement component selection menu
		// For now, just add a mesh renderer as example
		slot.AttachComponent<Core.Components.MeshRendererComponent>();
		RefreshInspector();
	}

	/// <summary>
	/// </summary>
	public override void _Process(double delta)
	{
		// Only update visible properties
		if (!Visible) return;

		// Refresh inspector at lower rate (e.g., 10 Hz instead of 60 Hz)
	}
}
