// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.UI.Xaml.Controls;

namespace TableViewSamples.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        // Wire feedback hyperlinks from BuildInfo so the URLs update every
        // build without re-editing XAML. See Generate-BuildInfo.ps1.
        LoopFeedbackLink.NavigateUri = new Uri(BuildInfo.LoopReportUrl);
        CumulativePrLink.NavigateUri = new Uri(BuildInfo.CumulativePrUrl);
    }
}
