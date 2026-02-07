namespace TestProject;

/// <summary>
/// A simple test class for basic testing
/// </summary>
public class SimpleClass
{
    private int _id;

    public SimpleClass(int id)
    {
        _id = id;
    }

    public int Id => _id;

    public void Process()
    {
        // Simple processing
    }

    public string GetData()
    {
        return "Sample data";
    }
}
