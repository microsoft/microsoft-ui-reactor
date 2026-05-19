using Microsoft.UI.Reactor;

ReactorApp.Run<AnimatedListDemo.App>("Animated List Demo", width: 760, height: 720
#if DEBUG
    , devtools: true
#endif
);
