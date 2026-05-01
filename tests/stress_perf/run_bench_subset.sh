#!/bin/bash
# Subset runner: 10/50/100% @ 7s each, ARM64 Release, all 6 variants.
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

DURATION=7
OUTFILE="$SCRIPT_DIR/benchmark_results_subset.csv"
CONFIG="Release"
PLATFORM="ARM64"
TFM_WINUI="net9.0-windows10.0.22621.0"
TFM_WPF="net9.0-windows"
STRESS_DIR="$REPO_ROOT/tests/stress_perf"

DIRECT_EXE="$STRESS_DIR/StressPerf.Direct/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Direct.exe"
BOUND_EXE="$STRESS_DIR/StressPerf.Bound/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Bound.exe"
REACTOR_EXE="$STRESS_DIR/StressPerf.Reactor/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Reactor.exe"
REACTORGRID_EXE="$STRESS_DIR/StressPerf.ReactorGrid/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.ReactorGrid.exe"
WPF_EXE="$STRESS_DIR/StressPerf.Wpf/bin/$PLATFORM/$CONFIG/$TFM_WPF/StressPerf.Wpf.exe"
DIRECTX_EXE="$STRESS_DIR/StressPerf.DirectX/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.DirectX.exe"

echo "App,Percent,Duration_s,Avg_FPS,Min_FPS,Max_FPS,Avg_Update_ms,Max_Update_ms,Avg_Memory_MB,Peak_Memory_MB" > "$OUTFILE"

parse_report() {
    local file="$1" app="$2" pct="$3"
    if [ ! -f "$file" ]; then
        echo "$app,$pct,0,0,0,0,0,0,0,0" >> "$OUTFILE"
        return
    fi
    local duration=$(grep "Duration:" "$file" | awk '{print $NF}' | tr -d 's')
    local avg_fps=$(grep "Avg FPS:" "$file" | awk '{print $NF}')
    local min_fps=$(grep "Min FPS:" "$file" | awk '{print $NF}')
    local max_fps=$(grep "Max FPS:" "$file" | awk '{print $NF}')
    local avg_update=$(grep "Avg Update:" "$file" | awk '{print $(NF-1)}')
    local max_update=$(grep "Max Update:" "$file" | awk '{print $(NF-1)}')
    local avg_mem=$(grep "Avg Memory:" "$file" | awk '{print $(NF-1)}')
    local peak_mem=$(grep "Peak Memory:" "$file" | awk '{print $(NF-1)}')
    echo "$app,$pct,$duration,$avg_fps,$min_fps,$max_fps,$avg_update,$max_update,$avg_mem,$peak_mem" >> "$OUTFILE"
}

run_app() {
    local exe="$1" name="$2" pct="$3"
    local exe_dir
    exe_dir=$(dirname "$exe")
    if [ ! -f "$exe" ]; then
        echo "  SKIP $name (not built: $exe)"
        echo "$name,$pct,0,0,0,0,0,0,0,0" >> "$OUTFILE"
        return
    fi
    find "$exe_dir" -maxdepth 1 -iname "*.report.txt" -delete 2>/dev/null || true
    echo "  Running $name @ ${pct}%..."
    "$exe" --headless --percent "$pct" --duration "$DURATION" 2>/dev/null || true
    local report_file
    report_file=$(find "$exe_dir" -maxdepth 1 -iname "*.report.txt" -type f 2>/dev/null | head -1)
    [ -z "$report_file" ] && report_file=$(find "$exe_dir/.." -iname "*.report.txt" -type f 2>/dev/null | head -1)
    parse_report "$report_file" "$name" "$pct"
}

echo "=== StressPerf Subset (ARM64 Release, 7s per run) ==="
for pct in 10 50 100; do
    echo "--- ${pct}% ---"
    run_app "$WPF_EXE"          "WPF.Direct"         "$pct"
    run_app "$DIRECT_EXE"       "WinUI.Direct"       "$pct"
    run_app "$BOUND_EXE"        "WinUI.Bound"        "$pct"
    run_app "$REACTOR_EXE"      "WinUI.Reactor"      "$pct"
    run_app "$REACTORGRID_EXE"  "WinUI.ReactorGrid"  "$pct"
    run_app "$DIRECTX_EXE"      "WinUI.DirectX"      "$pct"
    echo ""
done

echo "=== Done ==="
cat "$OUTFILE"
