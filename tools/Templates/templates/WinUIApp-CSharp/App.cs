#if (csharpFeature_TopLevelProgram)
ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600);

#else
namespace Company.ReactorApp1;

class Program
{
    static void Main(string[] args)
    {
        ReactorApp.Run<App>("Company.ReactorApp1", width: 900, height: 600);
    }
}

#endif
class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        return VStack(
            Heading($"Hello, {name}!"),
            TextField(name, setName, placeholder: "Your name").AutomationName("NameInput")
        );
    }
}