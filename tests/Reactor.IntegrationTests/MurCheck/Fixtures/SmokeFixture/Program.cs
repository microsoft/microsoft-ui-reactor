// Deliberately broken: f.Brr() trips CS1061 on Foo. Used by the mur-check
// smoke integration test under tests/Reactor.IntegrationTests/MurCheck/.
namespace SmokeFixture;

public class Foo
{
    public void Bar() { }
}

public static class Entry
{
    public static void Run()
    {
        var f = new Foo();
        f.Brr(); // CS1061
    }
}
