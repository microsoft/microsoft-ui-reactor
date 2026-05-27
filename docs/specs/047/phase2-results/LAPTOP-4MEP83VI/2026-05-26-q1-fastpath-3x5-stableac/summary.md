# Per-(bench, variant) means

| Bench | Variant | n | Mean ns | 95% CI ±ns | Mean alloc B | 95% CI ±B |
|---|---|---:|---:|---:|---:|---:|
| M1 | ReactorToday | 15 | 57,511 | 9,703 | 10,826,586 | 1,933 |
| M1 | ReactorV2 | 15 | 67,526 | 5,246 | 10,826,793 | 1,876 |
| M1 | ReactorDescriptors | 15 | 66,835 | 5,626 | 10,826,675 | 1,825 |
| | | | | | | |
| M2 | ReactorToday | 15 | 116,937 | 5,033 | 41,302,485 | 1,556,943 |
| M2 | ReactorV2 | 15 | 124,611 | 8,060 | 36,356,879 | 3,120,663 |
| M2 | ReactorDescriptors | 15 | 136,630 | 8,792 | 34,643,543 | 2,752,522 |
| | | | | | | |
| M5 | ReactorToday | 15 | 181,448 | 13,448 | 20,288,480 | 672,909 |
| M5 | ReactorV2 | 15 | 195,289 | 17,074 | 21,883,449 | 2,140,717 |
| M5 | ReactorDescriptors | 15 | 190,829 | 21,419 | 19,728,856 | 461,250 |
| | | | | | | |
| M7 | ReactorToday | 15 | 24,050 | 6,395 | 779,584 | 0 |
| M7 | ReactorV2 | 15 | 15,844 | 864 | 954,354 | 342,548 |
| M7 | ReactorDescriptors | 15 | 17,124 | 627 | 779,584 | 0 |
| | | | | | | |
| M10 | ReactorToday | 15 | 137,467 | 9,151 | 35,197,206 | 548,243 |
| M10 | ReactorV2 | 15 | 138,289 | 9,360 | 32,252,268 | 1,093,962 |
| M10 | ReactorDescriptors | 15 | 164,918 | 14,371 | 34,824,241 | 2,324,382 |
| | | | | | | |

# Q1 head-to-head — ReactorDescriptors deltas

| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | vs ReactorToday ns | vs ReactorToday alloc | Q1 band |
|---|---:|---:|---:|---:|---|
| M1 | -1.0% | -0.0% | +16.2% | +0.0% | <=5%: ship descriptors |
| M2 | +9.6% | -4.7% | +16.8% | -16.1% | 5-15%: judgment call |
| M5 | -2.3% | -9.8% | +5.2% | -2.8% | <=5%: ship descriptors |
| M7 | +8.1% | -18.3% | -28.8% | +0.0% | 5-15%: judgment call |
| M10 | +19.3% | +8.0% | +20.0% | -1.1% | >15%: ship hand-coded |
