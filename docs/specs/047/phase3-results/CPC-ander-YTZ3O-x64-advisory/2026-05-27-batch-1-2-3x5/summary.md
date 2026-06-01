# Per-(bench, variant) means

| Bench | Variant | n | Mean ns | 95% CI �ns | Mean alloc B | 95% CI �B |
|---|---|---:|---:|---:|---:|---:|
| M1 | ReactorToday | 15 | 109,915 | 3,467 | 10,826,905 | 1,886 |
| M1 | ReactorV2 | 15 | 108,093 | 2,926 | 10,826,801 | 1,844 |
| M1 | ReactorDescriptors | 15 | 107,571 | 2,902 | 10,826,675 | 1,825 |
| | | | | | | |
| M2 | ReactorToday | 15 | 182,601 | 16,275 | 38,224,073 | 281,894 |
| M2 | ReactorV2 | 15 | 185,375 | 15,496 | 34,354,931 | 110,173 |
| M2 | ReactorDescriptors | 15 | 185,291 | 16,185 | 35,242,613 | 120,632 |
| | | | | | | |
| M5 | ReactorToday | 15 | 270,993 | 35,734 | 20,352,679 | 60,871 |
| M5 | ReactorV2 | 15 | 274,432 | 38,551 | 20,331,266 | 75,615 |
| M5 | ReactorDescriptors | 15 | 271,888 | 37,536 | 19,920,690 | 71,038 |
| | | | | | | |
| M7 | ReactorToday | 15 | 21,795 | 110 | 801,443 | 29,193 |
| M7 | ReactorV2 | 15 | 21,989 | 211 | 801,443 | 29,193 |
| M7 | ReactorDescriptors | 15 | 21,815 | 97 | 801,443 | 29,193 |
| | | | | | | |
| M10 | ReactorToday | 15 | 137,855 | 6,268 | 41,926,601 | 1,783,924 |
| M10 | ReactorV2 | 15 | 144,075 | 5,052 | 34,167,103 | 1,127,117 |
| M10 | ReactorDescriptors | 15 | 137,522 | 5,870 | 35,671,089 | 1,084,347 |
| | | | | | | |

# Q1 head-to-head � ReactorDescriptors deltas

| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | vs ReactorToday ns | vs ReactorToday alloc | Q1 band |
|---|---:|---:|---:|---:|---|
| M1 | -0.5% | -0.0% | -2.1% | -0.0% | <=5%: ship descriptors |
| M2 | -0.0% | +2.6% | +1.5% | -7.8% | <=5%: ship descriptors |
| M5 | -0.9% | -2.0% | +0.3% | -2.1% | <=5%: ship descriptors |
| M7 | -0.8% | +0.0% | +0.1% | +0.0% | <=5%: ship descriptors |
| M10 | -4.5% | +4.4% | -0.2% | -14.9% | <=5%: ship descriptors |
