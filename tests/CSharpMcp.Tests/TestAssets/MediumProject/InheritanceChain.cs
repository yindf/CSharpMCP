namespace TestProject.Inheritance;

/// <summary>
/// Base class for inheritance testing
/// </summary>
public abstract class BaseController
{
    public virtual void Initialize()
    {
        // Base initialization
    }

    public abstract void Process();
}

/// <summary>
/// Derived controller class
/// </summary>
public class DerivedController : BaseController
{
    public override void Initialize()
    {
        base.Initialize();
        // Additional initialization
    }

    public override void Process()
    {
        // Process implementation
    }

    public void Execute()
    {
        Initialize();
        Process();
    }
}
