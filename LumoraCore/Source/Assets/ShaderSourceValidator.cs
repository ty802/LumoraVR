// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets;

// Sandbox gate for user-supplied shader source. Shader source SYNCS: a remote peer's custom material
// arrives over the network and THIS client compiles it, so validation must run on every peer right
// before the source is handed to the platform (CustomShaderMaterial.UpdateMaterial), never just at
// import. Rejects the abuse vectors we cannot afford on someone else's GPU: unbounded/oversized loops
// (device hangs), fullscreen-every-frame shader types, screen/depth capture hints (a world object must
// not get to read what the viewer sees), preprocessor includes, and project-scope global uniforms.
// Everything is static source analysis; no fabricated cost numbers. - xlinka
public static class ShaderSourceValidator
{
    public const int MaxSourceBytes = 64 * 1024;
    public const int MaxUniforms = 64;
    public const int MaxTextureSamples = 32;
    public const int MaxLoops = 8;
    public const int MaxLoopBound = 256;
    public const int MaxNestedLoopProduct = 1024;

    public sealed class Result
    {
        public bool IsValid => Errors.Count == 0;
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public int LoopCount;
        public int MaxLoopBoundSeen;
        public int TextureSampleCount;
        public int UniformCount;
        public int SourceBytes;
        public string ShaderType = "";
    }

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.Compiled);
    private static readonly Regex ShaderTypeRegex = new(@"shader_type\s+(\w+)\s*;", RegexOptions.Compiled);
    // Literal-bound counted loop: for (...; i < 64; ...). Anything loop-shaped that does not match this
    // (uniform-driven bounds, while, do) is rejected - a bound we cannot read is a bound the GPU pays.
    private static readonly Regex ForRegex = new(@"\bfor\s*\([^;{}]*;\s*\w+\s*(?:<|<=)\s*(?<bound>\d+)[^)]*\)", RegexOptions.Compiled);
    private static readonly Regex AnyForRegex = new(@"\bfor\s*\(", RegexOptions.Compiled);
    private static readonly Regex WhileRegex = new(@"\b(?:while|do)\b", RegexOptions.Compiled);
    private static readonly Regex TextureCallRegex = new(@"\b(?:texture|textureLod|texelFetch|textureGrad|textureProj)\s*\(", RegexOptions.Compiled);
    private static readonly Regex UniformRegex = new(@"^\s*(?:instance\s+)?uniform\b", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly string[] ForbiddenTokens =
    {
        "hint_screen_texture",
        "hint_depth_texture",
        "hint_normal_roughness_texture",
        "SCREEN_TEXTURE",
        "DEPTH_TEXTURE",
    };

    public static Result Validate(string? source)
    {
        var result = new Result();
        if (string.IsNullOrWhiteSpace(source))
        {
            result.Errors.Add("empty shader source");
            return result;
        }

        result.SourceBytes = source.Length;
        if (source.Length > MaxSourceBytes)
        {
            result.Errors.Add($"source too large ({source.Length / 1024} KB, max {MaxSourceBytes / 1024} KB)");
            return result;
        }

        string code = LineComment.Replace(BlockComment.Replace(source, " "), " ");

        var typeMatch = ShaderTypeRegex.Match(code);
        result.ShaderType = typeMatch.Success ? typeMatch.Groups[1].Value : "";
        if (result.ShaderType != "spatial" && result.ShaderType != "canvas_item")
        {
            // sky/fog run fullscreen every frame regardless of scene content; particles is its own
            // dispatch pipeline. Neither belongs in a droppable world material.
            result.Errors.Add(typeMatch.Success
                ? $"shader_type '{result.ShaderType}' is not allowed (spatial or canvas_item only)"
                : "missing shader_type declaration");
        }

        if (code.Contains('#'))
            result.Errors.Add("preprocessor directives (#include etc.) are not allowed");

        if (Regex.IsMatch(code, @"\bglobal\s+uniform\b"))
            result.Errors.Add("global uniforms are not allowed");

        foreach (var token in ForbiddenTokens)
        {
            if (code.Contains(token, StringComparison.Ordinal))
                result.Errors.Add($"'{token}' is not allowed (screen/depth capture)");
        }

        if (WhileRegex.IsMatch(code))
            result.Errors.Add("while/do loops are not allowed (unbounded on the GPU)");

        ValidateLoops(code, result);

        result.TextureSampleCount = TextureCallRegex.Matches(code).Count;
        if (result.TextureSampleCount > MaxTextureSamples)
            result.Errors.Add($"too many texture samples ({result.TextureSampleCount}, max {MaxTextureSamples})");
        else if (result.TextureSampleCount > MaxTextureSamples / 2)
            result.Warnings.Add($"heavy texture sampling ({result.TextureSampleCount} calls)");

        result.UniformCount = UniformRegex.Matches(code).Count;
        if (result.UniformCount > MaxUniforms)
            result.Errors.Add($"too many uniforms ({result.UniformCount}, max {MaxUniforms})");

        return result;
    }

    // Every for-loop must carry a readable literal bound; nested loops multiply, so track the brace
    // depth each loop's `for` keyword sat at and take the product of the still-open ones. A loop is
    // pushed with the depth OUTSIDE its own body (D); its body runs at D+1 and closes back to D, so on
    // a closing brace we pop everything pushed at or above the post-decrement depth (>=, not >) -
    // that's what actually reaches D again. Popping with a strict `>` leaves every depth-0 (top-level)
    // loop on the stack forever, since no `}` ever brings depth below 0: two unrelated sequential
    // loops at the same depth would then wrongly multiply against each other. Braceless
    // single-statement loop bodies (no `{`/`}` at all) never trigger a pop and stay on the stack for
    // the rest of the source - an acknowledged conservative over-count, not a correctness bug (it can
    // only over-reject, never let a bomb through). The depth tracking itself is an approximation (it
    // trusts braces, not full parsing), same reasoning. - xlinka
    private static void ValidateLoops(string code, Result result)
    {
        var literalLoops = new List<(int index, int bound)>();
        foreach (Match m in ForRegex.Matches(code))
            literalLoops.Add((m.Index, int.Parse(m.Groups["bound"].Value)));

        int allLoops = AnyForRegex.Matches(code).Count;
        result.LoopCount = allLoops;
        if (allLoops > literalLoops.Count)
        {
            result.Errors.Add("every for loop must use a literal integer bound (i < 64 style)");
            return;
        }
        if (allLoops > MaxLoops)
        {
            result.Errors.Add($"too many loops ({allLoops}, max {MaxLoops})");
            return;
        }

        var open = new Stack<(int depth, int bound)>();
        int loopIdx = 0;
        int depth = 0;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                while (open.Count > 0 && open.Peek().depth >= depth)
                    open.Pop();
            }

            if (loopIdx < literalLoops.Count && literalLoops[loopIdx].index == i)
            {
                int bound = literalLoops[loopIdx].bound;
                loopIdx++;
                if (bound > result.MaxLoopBoundSeen)
                    result.MaxLoopBoundSeen = bound;
                if (bound > MaxLoopBound)
                {
                    result.Errors.Add($"loop bound {bound} exceeds max {MaxLoopBound}");
                    return;
                }
                long product = bound;
                foreach (var (_, b) in open)
                    product *= b;
                if (product > MaxNestedLoopProduct)
                {
                    result.Errors.Add($"nested loops multiply to {product} iterations (max {MaxNestedLoopProduct})");
                    return;
                }
                open.Push((depth, bound));
            }
        }
    }
}
