using Forterm.Core;
var plan=LaunchPlan.Parse(args,"forterm-diagnose");
Console.WriteLine(plan.ToString()); // No raw args, endpoint, identity, title or credentials.
return plan.Error.Length==0?0:2;
