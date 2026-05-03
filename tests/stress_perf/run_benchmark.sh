#!/bin/bash
# StressPerf Benchmark Suite
# Runs all 6 variants at 10%-100% update rates, 7s each, outputs CSV.

set -e

# Discover the repo root — this script lives at tests/stress_perf/run_benchmark.sh
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

DURATION=7
OUTFILE="$SCRIPT_DIR/benchmark_results.csv"

# Detect platform (x64 or ARM64) — prefer ARM64 if both exist
detect_platform() {
    local base_dir="$1"
    if [ -d "$base_dir/ARM64" ]; then
        echo "ARM64"
    elif [ -d "$base_dir/x64" ]; then
        echo "x64"
    else
        echo "ARM64"  # default
    fi
}

# Build configuration
CONFIG="Release"
TFM_WINUI="net9.0-windows10.0.22621.0"
TFM_WPF="net9.0-windows"

STRESS_DIR="$REPO_ROOT/tests/stress_perf"

# Detect platform from the first available build output
PLATFORM=$(detect_platform "$STRESS_DIR/StressPerf.Direct/bin")

DIRECT_EXE="$STRESS_DIR/StressPerf.Direct/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Direct.exe"
BOUND_EXE="$STRESS_DIR/StressPerf.Bound/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Bound.exe"
REACTOR_EXE="$STRESS_DIR/StressPerf.Reactor/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.Reactor.exe"
REACTOROPT_EXE="$STRESS_DIR/StressPerf.ReactorOptimized/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.ReactorOptimized.exe"
REACTORGRID_EXE="$STRESS_DIR/StressPerf.ReactorGrid/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.ReactorGrid.exe"
WPF_EXE="$STRESS_DIR/StressPerf.Wpf/bin/$PLATFORM/$CONFIG/$TFM_WPF/StressPerf.Wpf.exe"
DIRECTX_EXE="$STRESS_DIR/StressPerf.DirectX/bin/$PLATFORM/$CONFIG/$TFM_WINUI/StressPerf.DirectX.exe"

# CSV header
echo "App,Percent,Duration_s,Avg_FPS,Min_FPS,Max_FPS,Avg_Update_ms,Max_Update_ms,Avg_Memory_MB,Peak_Memory_MB" > "$OUTFILE"

parse_report() {
    local file="$1"
    local app="$2"
    local pct="$3"

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
    local exe="$1"
    local name="$2"
    local pct="$3"
    local exe_dir
    exe_dir=$(dirname "$exe")

    if [ ! -f "$exe" ]; then
        echo "  SKIP $name (not built: $exe)"
        echo "$name,$pct,0,0,0,0,0,0,0,0" >> "$OUTFILE"
        return
    fi

    # Delete old report files
    find "$exe_dir" -maxdepth 1 -iname "*.report.txt" -delete 2>/dev/null || true

    echo "  Running $name @ ${pct}%..."
    "$exe" --headless --percent "$pct" --duration "$DURATION" 2>/dev/null || true

    # Find report file (case-insensitive to handle ARM64/arm64 mismatch)
    local report_file
    report_file=$(find "$exe_dir" -maxdepth 1 -iname "*.report.txt" -type f 2>/dev/null | head -1)
    [ -z "$report_file" ] && report_file=$(find "$exe_dir/.." -iname "*.report.txt" -type f 2>/dev/null | head -1)

    parse_report "$report_file" "$name" "$pct"
}

echo "=== StressPerf Benchmark Suite ==="
echo "Repo root: $REPO_ROOT"
echo "Platform:  $PLATFORM"
echo "Duration per run: ${DURATION}s"
echo "Output: $OUTFILE"
echo ""

for pct in 10 20 30 40 50 60 70 80 90 100; do
    echo "--- ${pct}% update rate ---"
    run_app "$WPF_EXE"      "WPF.Direct"      "$pct"
    run_app "$DIRECT_EXE"    "WinUI.Direct"    "$pct"
    run_app "$BOUND_EXE"     "WinUI.Bound"     "$pct"
    run_app "$REACTOR_EXE"      "WinUI.Reactor"      "$pct"
    run_app "$REACTOROPT_EXE"   "WinUI.ReactorOptimized" "$pct"
    run_app "$REACTORGRID_EXE"  "WinUI.ReactorGrid"  "$pct"
    run_app "$DIRECTX_EXE"   "WinUI.DirectX"   "$pct"
    echo ""
done

echo "=== Done ==="
echo "Results written to: $OUTFILE"
echo ""
cat "$OUTFILE"
