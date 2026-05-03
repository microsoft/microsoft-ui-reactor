using System;
using System.Collections.Generic;
using System.Text;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Streaming, stateful parser for the model's <c>===STEP===/===CODE===/===DELTA===</c>
/// envelope (see <c>Resources/SystemPrompt.format.md</c>). Token chunks arrive
/// at arbitrary boundaries from the wire; the parser holds a small line buffer
/// so a marker split across two SSE frames is still recognised.
/// </summary>
public sealed class GeneratedOutputParser
{
    public event Action<int>? StepStarted;
    public event Action<int, string>? CodeBlockStarted;     // (stepNumber, relativePath)
    public event Action<int, string>? CodeChunk;            // (stepNumber, chunk)
    public event Action<int>? CodeBlockCompleted;
    public event Action<int>? DeltaStarted;
    public event Action<int, string>? DeltaChunk;
    public event Action<int>? StepCompleted;
    public event Action<string>? Warning;

    enum Mode { Outside, InsideStepHeader, AwaitingFenceOpen, InCode, InDelta }

    Mode _mode = Mode.Outside;
    int _currentStep;
    string? _currentCodePath;

    readonly StringBuilder _lineBuf = new();

    /// <summary>Feed a chunk of model output. The parser fires events as it crosses markers.</summary>
    public void Feed(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                ProcessLine(_lineBuf.ToString());
                _lineBuf.Clear();
            }
            else if (c != '\r')
            {
                _lineBuf.Append(c);
            }
        }
    }

    /// <summary>Signal end of stream — flush trailing buffered content and warn on partial steps.</summary>
    public void Complete()
    {
        if (_lineBuf.Length > 0)
        {
            ProcessLine(_lineBuf.ToString());
            _lineBuf.Clear();
        }

        if (_mode is Mode.InCode or Mode.InDelta or Mode.InsideStepHeader or Mode.AwaitingFenceOpen)
        {
            Warning?.Invoke($"Stream ended mid-step at step {_currentStep}. UI will show whatever arrived.");
            if (_mode == Mode.InCode) CodeBlockCompleted?.Invoke(_currentStep);
            StepCompleted?.Invoke(_currentStep);
            _mode = Mode.Outside;
        }
    }

    void ProcessLine(string line)
    {
        // Markers — recognised regardless of mode so we can recover from drift.
        if (TryMatchStepStart(line, out var n))
        {
            CloseAnythingOpen();
            _currentStep = n;
            _mode = Mode.InsideStepHeader;
            StepStarted?.Invoke(n);
            return;
        }

        if (TryMatchStepEnd(line, out var endN))
        {
            if (_mode == Mode.InCode) CodeBlockCompleted?.Invoke(_currentStep);
            _mode = Mode.Outside;
            StepCompleted?.Invoke(endN);
            _currentCodePath = null;
            return;
        }

        if (TryMatchCode(line, out var path))
        {
            if (_mode == Mode.InCode) CodeBlockCompleted?.Invoke(_currentStep);
            _currentCodePath = path;
            _mode = Mode.AwaitingFenceOpen;
            CodeBlockStarted?.Invoke(_currentStep, path);
            return;
        }

        if (line.Trim() == "===DELTA===")
        {
            if (_mode == Mode.InCode) CodeBlockCompleted?.Invoke(_currentStep);
            _mode = Mode.InDelta;
            DeltaStarted?.Invoke(_currentStep);
            return;
        }

        switch (_mode)
        {
            case Mode.AwaitingFenceOpen:
                // The first line after the CODE marker is the opening fence (```csharp etc.).
                // We tolerate any "```" prefix, including bare ``` on its own line.
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    _mode = Mode.InCode;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    // No fence — treat the line as the first line of code.
                    _mode = Mode.InCode;
                    CodeChunk?.Invoke(_currentStep, line + "\n");
                }
                return;

            case Mode.InCode:
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    CodeBlockCompleted?.Invoke(_currentStep);
                    _mode = Mode.InsideStepHeader; // remain inside this step until next marker
                    return;
                }
                CodeChunk?.Invoke(_currentStep, line + "\n");
                return;

            case Mode.InDelta:
                DeltaChunk?.Invoke(_currentStep, line + "\n");
                return;

            case Mode.InsideStepHeader:
            case Mode.Outside:
                // Pre-amble or between blocks — discard quietly.
                return;
        }
    }

    void CloseAnythingOpen()
    {
        if (_mode == Mode.InCode) CodeBlockCompleted?.Invoke(_currentStep);
        if (_mode is Mode.InCode or Mode.InDelta or Mode.AwaitingFenceOpen or Mode.InsideStepHeader)
            StepCompleted?.Invoke(_currentStep);
    }

    static bool TryMatchStepStart(string line, out int n)
    {
        n = 0;
        var t = line.Trim();
        if (!t.StartsWith("===STEP ", StringComparison.Ordinal) || !t.EndsWith("===", StringComparison.Ordinal))
            return false;
        var inner = t["===STEP ".Length..^"===".Length].Trim();
        return int.TryParse(inner, out n);
    }

    static bool TryMatchStepEnd(string line, out int n)
    {
        n = 0;
        var t = line.Trim();
        if (!t.StartsWith("===END STEP ", StringComparison.Ordinal) || !t.EndsWith("===", StringComparison.Ordinal))
            return false;
        var inner = t["===END STEP ".Length..^"===".Length].Trim();
        return int.TryParse(inner, out n);
    }

    static bool TryMatchCode(string line, out string path)
    {
        path = string.Empty;
        var t = line.Trim();
        const string prefix = "===CODE ";
        const string suffix = "===";
        if (!t.StartsWith(prefix, StringComparison.Ordinal) || !t.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        path = t[prefix.Length..^suffix.Length].Trim();
        return path.Length > 0;
    }
}
