using System;
using System.Collections.Generic;
using System.Linq;

namespace Aquamarine.Source.Core.WorldTemplates;

/// <summary>
/// Manages available world templates.
/// </summary>
public static class TemplateManager
{
	private static readonly List<WorldTemplate> _templates = new();
	private static bool _initialized = false;

	/// <summary>
	/// Get all available templates.
	/// </summary>
	public static IReadOnlyList<WorldTemplate> GetAllTemplates()
	{
		EnsureInitialized();
		return _templates.AsReadOnly();
	}

	/// <summary>
	/// Get templates by category.
	/// </summary>
	public static IEnumerable<WorldTemplate> GetTemplatesByCategory(string category)
	{
		EnsureInitialized();
		return _templates.Where(t => t.Category == category);
	}

	/// <summary>
	/// Get template by name.
	/// </summary>
	public static WorldTemplate GetTemplate(string name)
	{
		EnsureInitialized();
		return _templates.FirstOrDefault(t => t.Name == name);
	}

	/// <summary>
	/// Get all categories.
	/// </summary>
	public static IEnumerable<string> GetCategories()
	{
		EnsureInitialized();
		return _templates.Select(t => t.Category).Distinct();
	}

	/// <summary>
	/// Register a custom template.
	/// </summary>
	public static void RegisterTemplate(WorldTemplate template)
	{
		EnsureInitialized();
		if (!_templates.Contains(template))
		{
			_templates.Add(template);
		}
	}

	private static void EnsureInitialized()
	{
		if (_initialized) return;

		// Register built-in templates
		_templates.Add(new GridTemplate());
		_templates.Add(new EmptyTemplate());

		_initialized = true;
	}
}
