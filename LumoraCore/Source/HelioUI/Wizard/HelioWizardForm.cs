using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.HelioUI;

/// <summary>
/// Base class for wizard-style multi-step UI forms.
/// Provides navigation stack and panel management for wizard flows.
/// </summary>
[ComponentCategory("HelioUI/Wizard")]
public abstract class HelioWizardForm : Component
{
	// ===== REFERENCES =====

	/// <summary>
	/// The swap panel used for transitions.
	/// </summary>
	protected SyncRef<HelioSwapPanel> SwapPanel { get; private set; }

	/// <summary>
	/// The canvas containing this wizard.
	/// </summary>
	protected SyncRef<HelioCanvas> Canvas { get; private set; }

	/// <summary>
	/// The content slot where panels are created.
	/// </summary>
	protected SyncRef<Slot> ContentSlot { get; private set; }

	// ===== CONFIGURATION =====

	/// <summary>
	/// Size of the wizard canvas.
	/// </summary>
	protected virtual float2 CanvasSize => new float2(400f, 600f);

	/// <summary>
	/// Pixels per unit for this canvas. Higher values = smaller physical size.
	/// For 0.8m tall panel with 600px height: 600/0.8 = 750 px/unit
	/// </summary>
	protected virtual float WizardPixelScale => 750f;

	/// <summary>
	/// Duration of panel transitions.
	/// </summary>
	protected virtual float TransitionDuration => 0.25f;

	/// <summary>
	/// Title displayed at the top of the wizard.
	/// </summary>
	protected virtual string WizardTitle => "Wizard";

	// ===== STATE =====

	/// <summary>
	/// Navigation stack of step builders.
	/// </summary>
	protected readonly List<Action<HelioUIBuilder>> StepStack = new();

	/// <summary>
	/// Currently active step index.
	/// </summary>
	public int CurrentStepIndex => StepStack.Count - 1;

	/// <summary>
	/// Whether there are previous steps to return to.
	/// </summary>
	public bool CanReturn => StepStack.Count > 1;

	// ===== INITIALIZATION =====

	public override void OnAwake()
	{
		base.OnAwake();

		SwapPanel = new SyncRef<HelioSwapPanel>(this);
		Canvas = new SyncRef<HelioCanvas>(this);
		ContentSlot = new SyncRef<Slot>(this);
	}

	/// <summary>
	/// Called when the wizard starts. Sets up the UI structure.
	/// </summary>
	public override void OnStart()
	{
		base.OnStart();
		try
		{
			SetupWizard();
		}
		catch (System.Exception ex)
		{
			Logging.Logger.Error($"HelioWizardForm.OnStart: Failed to setup wizard: {ex.Message}");
		}
	}

	/// <summary>
	/// Set up the wizard UI structure.
	/// </summary>
	protected virtual void SetupWizard()
	{
		// Create canvas if not exists
		var canvasSlot = Slot.AddSlot("WizardCanvas");
		var canvas = canvasSlot.AttachComponent<HelioCanvas>();
		canvas.ReferenceSize.Value = CanvasSize;
		canvas.PixelScale.Value = WizardPixelScale;
		Canvas.Target = canvas;

		// Add rect transform to canvas
		var canvasRect = canvasSlot.AttachComponent<HelioRectTransform>();
		canvasRect.AnchorMin.Value = float2.Zero;
		canvasRect.AnchorMax.Value = float2.One;

		// Create content container
		var contentSlot = canvasSlot.AddSlot("Content");
		var contentRect = contentSlot.AttachComponent<HelioRectTransform>();
		contentRect.AnchorMin.Value = float2.Zero;
		contentRect.AnchorMax.Value = float2.One;
		ContentSlot.Target = contentSlot;

		// Create swap panel for transitions
		var swapPanel = contentSlot.AttachComponent<HelioSwapPanel>();
		swapPanel.TransitionDuration.Value = TransitionDuration;
		SwapPanel.Target = swapPanel;

		// Build the root step
		Open(BuildRootStep);
	}

	// ===== NAVIGATION =====

	/// <summary>
	/// Navigate to a new step.
	/// </summary>
	public void Open(Action<HelioUIBuilder> buildStep)
	{
		if (buildStep == null) return;

		StepStack.Add(buildStep);
		BuildCurrentStep(SwapDirection.Left);
	}

	/// <summary>
	/// Return to the previous step.
	/// </summary>
	public void Return()
	{
		if (!CanReturn) return;

		StepStack.RemoveAt(StepStack.Count - 1);
		BuildCurrentStep(SwapDirection.Right);
	}

	/// <summary>
	/// Close the wizard entirely.
	/// </summary>
	public virtual void Close()
	{
		StepStack.Clear();
		Slot.Destroy();
	}

	/// <summary>
	/// Reset to the root step.
	/// </summary>
	public void Reset()
	{
		while (StepStack.Count > 1)
		{
			StepStack.RemoveAt(StepStack.Count - 1);
		}
		BuildCurrentStep(SwapDirection.Right);
	}

	// ===== STEP BUILDING =====

	/// <summary>
	/// Build the current step UI.
	/// </summary>
	private void BuildCurrentStep(SwapDirection direction)
	{
		if (StepStack.Count == 0) return;

		var contentSlot = ContentSlot.Target;
		if (contentSlot == null) return;

		// Create new panel for this step
		var panelSlot = contentSlot.AddSlot($"Step_{StepStack.Count}");
		var panelRect = panelSlot.AttachComponent<HelioRectTransform>();
		panelRect.AnchorMin.Value = float2.Zero;
		panelRect.AnchorMax.Value = float2.One;

		// Add a vertical layout to the step panel so all content is arranged properly
		var stepLayout = panelSlot.AttachComponent<HelioVerticalLayout>();
		stepLayout.Spacing.Value = new float2(4f, 4f);
		stepLayout.Padding.Value = new float4(8f, 8f, 8f, 8f);

		// Build the step content
		var builder = new HelioUIBuilder(panelSlot);
		StepStack[^1](builder);

		// Swap to new panel
		var swapPanel = SwapPanel.Target;
		if (swapPanel != null)
		{
			swapPanel.SwapTo(panelSlot, direction);
		}
	}

	/// <summary>
	/// Override in subclasses to build the initial wizard step.
	/// </summary>
	protected abstract void BuildRootStep(HelioUIBuilder ui);

	// ===== UTILITY =====

	/// <summary>
	/// Create a standard navigation footer with Back and Next buttons.
	/// </summary>
	protected void BuildNavigationFooter(HelioUIBuilder ui, string nextLabel = "Next", Action onNext = null, bool showBack = true)
	{
		ui.HorizontalLayout(spacing: 8f);

		if (showBack && CanReturn)
		{
			ui.Button("Back", Return);
		}

		ui.FlexibleSpacer();

		if (onNext != null)
		{
			ui.Button(nextLabel, onNext);
		}

		ui.EndLayout();
	}

	/// <summary>
	/// Create a standard header with title.
	/// </summary>
	protected void BuildHeader(HelioUIBuilder ui, string title = null)
	{
		ui.Text(title ?? WizardTitle, fontSize: 24f);
		ui.Spacer(16f);
	}
}
