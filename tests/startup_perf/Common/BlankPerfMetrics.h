#pragma once

#include <chrono>
#include <string>
#include "Tracing.h"

// Mirrors microsoft-ui-xaml-lift/.../Common/BlankPerfMetrics.h.
// Three timestamps, two ETW events. RecordFirstFrame fires FirstRender;
// RecordInteractive fires FirstIdle. Both are one-shot guarded so a noisy
// CompositionTarget::Rendered doesn't double-count.
class BlankPerfMetrics
{
public:
    void RecordAppStart() { m_appStart = Clock::now(); }

    void RecordFirstFrame()
    {
        if (!m_firstFrameRecorded)
        {
            m_firstFrame = Clock::now();
            m_firstFrameRecorded = true;
            Tracing::TraceFirstRender();
        }
    }

    void RecordInteractive()
    {
        if (!m_finalized && m_firstFrameRecorded)
        {
            m_interactive = Clock::now();
            m_finalized = true;
            Tracing::TraceFirstIdle();
        }
    }

    bool IsFinalized() const { return m_finalized; }
    bool IsFirstFrameRecorded() const { return m_firstFrameRecorded; }
    long long FirstFrameMs() const { return ElapsedMs(m_appStart, m_firstFrame); }
    long long InteractiveMs() const { return ElapsedMs(m_appStart, m_interactive); }

    std::wstring Summary() const
    {
        return L"First Frame: " + std::to_wstring(FirstFrameMs()) + L" ms"
             + L"  |  Interactive: " + std::to_wstring(InteractiveMs()) + L" ms";
    }

private:
    using Clock = std::chrono::steady_clock;
    using TimePoint = Clock::time_point;

    TimePoint m_appStart{};
    TimePoint m_firstFrame{};
    TimePoint m_interactive{};

    bool m_firstFrameRecorded = false;
    bool m_finalized = false;

    long long ElapsedMs(TimePoint from, TimePoint to) const
    {
        return std::chrono::duration_cast<std::chrono::milliseconds>(to - from).count();
    }
};
