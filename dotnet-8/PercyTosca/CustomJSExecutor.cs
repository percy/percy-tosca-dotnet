using Tricentis.Automation.AutomationInstructions.TestActions;
using Tricentis.Automation.Creation;
using Tricentis.Automation.Engines;
using Tricentis.Automation.Engines.SpecialExecutionTasks;
using Tricentis.Automation.Engines.SpecialExecutionTasks.Html;

namespace Percy.CustomJSExecutor;

public class CustomJSExecutor : ExecuteJavaScriptBase
{
    public CustomJSExecutor(Validator validator) : base(validator)
    {
    }

    public CustomJSExecutor(SpecialExecutionTaskContext context, Validator validator)
        : base(context, validator)
    {
    }

    public override ActionResult ExecuteScript(ISpecialExecutionTaskTestAction testAction)
    {
        throw new System.NotImplementedException(); 
    }
}