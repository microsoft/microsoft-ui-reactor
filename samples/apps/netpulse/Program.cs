using Microsoft.UI.Reactor;

ReactorApp.Run<NetPulse.App>("NetPulse — Network Traffic Visualizer", width: 1400, height: 920
#if DEBUG
    , devtools: true
#endif
);
