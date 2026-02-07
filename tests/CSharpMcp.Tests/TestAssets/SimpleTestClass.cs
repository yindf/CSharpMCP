namespace TestAssets;

/// <summary>
/// Test class for MCP server testing
/// </summary>
public class SimpleTestClass
{
    private readonly string _field;

    public SimpleTestClass(string field)
    {
        _field = field;
    }

    public string Property { get; set; }

    public void TestMethod()
    {
        Console.WriteLine("Test method");
    }

    public int Calculate(int a, int b)
    {
        return a + b;
    }

    private void HelperMethod()
    {
        // This is a helper method
    }
}

/// <summary>
/// Derived test class for inheritance testing
/// </summary>
public class DerivedTestClass : SimpleTestClass
{
    public DerivedTestClass() : base("test")
    {
    }

    public void NewMethod()
    {
        // New method in derived class
    }
}
