# Spec 047 §14 Phase 3 — final bulk-port advisory perf (n=15)

| Bench | Name | Today ns | V2 ns | Descr ns | D-vs-Today | D-vs-V2 | Descr alloc |
|---|---|---:|---:|---:|---:|---:|---:|
| M1 | Mount_Leaf_NoCallback | 115752 | 111171 | 133015 | +14.9% | +19.6% | 13,725,088 B |
| M2 | Mount_Leaf_OneCallback | 190032 | 197598 | 186767 | -1.7% | -5.5% | 35,450,120 B |
| M3 | Mount_Leaf_ThreeCallbacks | 771773 | 778007 | 796875 | +3.3% | +2.4% | 91,636,472 B |
| M4 | Dispatch_Switch_Cold | 232694 | 231387 | 183354 | -21.2% | -20.8% | 18,325,152 B |
| M5 | Dispatch_Switch_Warm | 240593 | 223797 | 182217 | -24.3% | -18.6% | 18,330,440 B |
| M6 | Dispatch_ExternalType | 97191 | 96454 | 97405 | +0.2% | +1.0% | 9,391,368 B |
| M7 | Update_NoChange | 22023 | 21949 | 23647 | +7.4% | +7.7% | 1,084,032 B |
| M8 | Update_OneLeafChanged | 6361 | 6324 | 7982 | +25.5% | +26.2% | 3,735,768 B |
| M9 | Update_AllChanged | 2207411 | 2228311 | 2287202 | +3.6% | +2.6% | 2,880,796,032 B |
| M10 | EventHandlerState_Alloc | 138231 | 146837 | 150296 | +8.7% | +2.4% | 34,066,824 B |
| M11 | ModifierEHS_Frequency | 8727 | 8954 | 9468 | +8.5% | +5.8% | 1,664,360 B |
| M12 | Pool_Rent_HotPath | 105863 | 106443 | 127979 | +20.9% | +20.2% | 12,912,216 B |
| M13 | Setters_Suppression_Scope | 35 | 35 | 34 | -0.9% | -2.1% | 245,728 B |
