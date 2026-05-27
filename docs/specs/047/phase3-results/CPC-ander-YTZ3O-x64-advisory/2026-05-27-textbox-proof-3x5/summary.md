# Per-(bench, variant) means

| Bench | Variant | n | Mean ns | 95% CI �ns | Mean alloc B | 95% CI �B |
|---|---|---:|---:|---:|---:|---:|
| M1 | ReactorToday | 15 | 112,904 | 3,478 | 10,826,886 | 1,914 |
| M1 | ReactorV2 | 15 | 110,963 | 2,808 | 10,826,782 | 1,832 |
| M1 | ReactorDescriptors | 15 | 109,936 | 2,932 | 10,826,675 | 1,825 |
| | | | | | | |
| M2 | ReactorToday | 15 | 182,247 | 16,047 | 38,271,946 | 236,604 |
| M2 | ReactorV2 | 15 | 191,687 | 15,736 | 34,330,992 | 111,967 |
| M2 | ReactorDescriptors | 15 | 187,499 | 14,811 | 35,496,122 | 194,087 |
| | | | | | | |
| M5 | ReactorToday | 15 | 278,100 | 36,458 | 20,371,873 | 68,857 |
| M5 | ReactorV2 | 15 | 287,099 | 41,209 | 20,350,523 | 78,089 |
| M5 | ReactorDescriptors | 15 | 289,916 | 36,743 | 20,283,272 | 87,428 |
| | | | | | | |
| M7 | ReactorToday | 15 | 22,351 | 125 | 801,443 | 29,193 |
| M7 | ReactorV2 | 15 | 22,567 | 207 | 801,443 | 29,193 |
| M7 | ReactorDescriptors | 15 | 22,240 | 77 | 801,443 | 29,193 |
| | | | | | | |
| M10 | ReactorToday | 15 | 137,322 | 6,456 | 41,921,840 | 1,824,366 |
| M10 | ReactorV2 | 15 | 148,807 | 6,369 | 33,798,270 | 1,015,934 |
| M10 | ReactorDescriptors | 15 | 150,409 | 6,903 | 35,380,849 | 1,220,536 |
| | | | | | | |

# Q1 head-to-head � ReactorDescriptors deltas

| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | vs ReactorToday ns | vs ReactorToday alloc | Q1 band |
|---|---:|---:|---:|---:|---|
| M1 | -0.9% | -0.0% | -2.6% | -0.0% | <=5%: ship descriptors |
| M2 | -2.2% | +3.4% | +2.9% | -7.3% | <=5%: ship descriptors |
| M5 | +1.0% | -0.3% | +4.2% | -0.4% | <=5%: ship descriptors |
| M7 | -1.4% | +0.0% | -0.5% | +0.0% | <=5%: ship descriptors |
| M10 | +1.1% | +4.7% | +9.5% | -15.6% | <=5%: ship descriptors |
