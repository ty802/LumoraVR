using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core;
using Lumora.Core.Logging;

namespace Lumora.Core.Input;

/// <summary>
/// Central manager for all input devices and drivers
/// Core input management system
/// </summary>
public class InputInterface
{
	private class UpdateBucket
	{
		public readonly List<IInputDriver> InputDrivers = new List<IInputDriver>();
		public readonly int Order;

		public UpdateBucket(int order)
		{
			Order = order;
		}
	}

	private Lumora.Core.Engine _engine;
	private List<IInputDriver> _inputDrivers = new List<IInputDriver>();
	private List<UpdateBucket> _inputDriverUpdateBuckets = new List<UpdateBucket>();
	private List<IInputDevice> _inputDevices = new List<IInputDevice>();

	// Standard devices
	public Mouse Mouse { get; private set; }

	// Driver interfaces
	private IKeyboardDriver _keyboardDriver;
	private IMouseDriver _mouseDriver;

	public InputInterface(Lumora.Core.Engine engine)
	{
		_engine = engine;
		Logger.Log("InputInterface: Initialized");
	}

	#region Driver Registration

	/// <summary>
	/// Register a generic input driver (for VR controllers, gamepads, etc.)
	/// Core input management system RegisterInputDriver (lines 1294-1306)
	/// </summary>
	public void RegisterInputDriver(IInputDriver driver)
	{
		_inputDrivers.Add(driver);
		driver.RegisterInputs(this);

		// Find or create UpdateBucket for this driver's order
		UpdateBucket bucket = _inputDriverUpdateBuckets.FirstOrDefault(b => b.Order == driver.UpdateOrder);
		if (bucket == null)
		{
			bucket = new UpdateBucket(driver.UpdateOrder);
			_inputDriverUpdateBuckets.Add(bucket);
			// Keep buckets sorted by order
			_inputDriverUpdateBuckets.Sort((a, b) => a.Order.CompareTo(b.Order));
		}
		bucket.InputDrivers.Add(driver);

		Logger.Log($"InputInterface: Registered driver with UpdateOrder {driver.UpdateOrder}");
	}

	/// <summary>
	/// Register the keyboard driver
	/// Core input management system RegisterKeyboardDriver (lines 1173-1181)
	/// </summary>
	public void RegisterKeyboardDriver(IKeyboardDriver keyboardDriver)
	{
		if (_keyboardDriver != null)
			throw new InvalidOperationException("Keyboard Driver is already registered");

		_keyboardDriver = keyboardDriver;
		Logger.Log("InputInterface: Keyboard driver registered");
	}

	/// <summary>
	/// Register the mouse driver and create the Mouse device
	/// Core input management system RegisterMouseDriver (lines 1183-1193)
	/// </summary>
	public void RegisterMouseDriver(IMouseDriver mouseDriver)
	{
		if (_mouseDriver != null)
			throw new InvalidOperationException("Mouse Driver already registered!");

		Mouse = new Mouse();
		RegisterInputDevice(Mouse, "Mouse");
		_mouseDriver = mouseDriver;

		Logger.Log("InputInterface: Mouse driver registered");
	}

	/// <summary>
	/// Register an input device (mouse, controller, etc.)
	/// </summary>
	public void RegisterInputDevice(IInputDevice device, string name)
	{
		int deviceIndex = _inputDevices.Count;
		_inputDevices.Add(device);
		device.Initialize(this, deviceIndex, name);

		Logger.Log($"InputInterface: Registered device '{name}' (index {deviceIndex})");
	}

	#endregion

	#region Update Loop

	/// <summary>
	/// Update all input drivers and devices
	/// Core input management system UpdateInputs (lines 881-887)
	/// </summary>
	public void UpdateInputs(float deltaTime)
	{
		// Update drivers in order by their UpdateOrder buckets
		foreach (var bucket in _inputDriverUpdateBuckets)
		{
			foreach (var driver in bucket.InputDrivers)
			{
				driver.UpdateInputs(deltaTime);
			}
		}

		// Update mouse through driver
		if (_mouseDriver != null && Mouse != null)
		{
			_mouseDriver.UpdateMouse(Mouse);
		}
	}

	#endregion

	#region Device Access

	public IInputDevice GetDevice(int index)
	{
		if (index < 0 || index >= _inputDevices.Count)
			return null;
		return _inputDevices[index];
	}

	public T GetDevice<T>(string name) where T : class, IInputDevice
	{
		return _inputDevices.FirstOrDefault(d => d.Name == name) as T;
	}

	public IKeyboardDriver GetKeyboardDriver() => _keyboardDriver;

	#endregion
}
