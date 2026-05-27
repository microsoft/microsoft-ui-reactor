# Per-(bench, variant) means

| Bench | Variant | n | Mean ns | 95% CI ±ns | Mean alloc B | 95% CI ±B |
|---|---|---:|---:|---:|---:|---:|
| M1 | ReactorToday | 15 | 39,417 | 3,689 | 10,852,725 | 51,885 |
| M1 | ReactorV2 | 15 | 44,860 | 6,575 | 10,826,846 | 1,885 |
| M1 | ReactorDescriptors | 15 | 55,419 | 8,547 | 10,826,675 | 1,825 |
| | | | | | | |
| M2 | ReactorToday | 15 | 78,051 | 7,486 | 40,894,779 | 1,981,937 |
| M2 | ReactorV2 | 15 | 105,492 | 24,209 | 36,276,095 | 3,815,954 |
| M2 | ReactorDescriptors | 15 | 125,277 | 9,039 | 40,934,777 | 7,171,763 |
| | | | | | | |
| M5 | ReactorToday | 15 | 114,555 | 25,502 | 20,979,348 | 1,216,439 |
| M5 | ReactorV2 | 15 | 114,245 | 22,826 | 20,974,136 | 1,551,036 |
| M5 | ReactorDescriptors | 15 | 133,346 | 20,246 | 23,021,730 | 4,645,056 |
| | | | | | | |
| M7 | ReactorToday | 15 | 19,604 | 1,968 | 779,584 | 0 |
| M7 | ReactorV2 | 15 | 17,707 | 1,740 | 779,584 | 0 |
| M7 | ReactorDescriptors | 15 | 16,631 | 1,806 | 779,584 | 0 |
| | | | | | | |
| M10 | ReactorToday | 15 | 156,894 | 17,079 | 35,195,839 | 743,216 |
| M10 | ReactorV2 | 15 | 166,575 | 24,280 | 31,562,516 | 750,655 |
| M10 | ReactorDescriptors | 15 | 220,126 | 41,598 | 33,258,249 | 1,492,779 |
| | | | | | | |

# Q1 head-to-head — ReactorDescriptors deltas

| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | vs ReactorToday ns | vs ReactorToday alloc | Q1 band |
|---|---:|---:|---:|---:|---|
| M1 | +23.5% | -0.0% | +40.6% | -0.2% | >15%: ship hand-coded |
| M2 | +18.8% | +12.8% | +60.5% | +0.1% | >15%: ship hand-coded |
| M5 | +16.7% | +9.8% | +16.4% | +9.7% | >15%: ship hand-coded |
| M7 | -6.1% | +0.0% | -15.2% | +0.0% | 5-15%: judgment call |
| M10 | +32.1% | +5.4% | +40.3% | -5.5% | >15%: ship hand-coded |
