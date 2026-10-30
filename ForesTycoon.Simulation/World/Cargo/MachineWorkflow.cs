namespace ForesTycoon;

// Numeric values are the existing machine.workflow graph contract, not persisted machine states.
internal enum MachineWorkflowOperation
{
    Continue = 0,
    Wait = 1,
    ReturnHome = 2,
    Deliver = 3,
    GoToWork = 4,
    SearchForWork = 5
}

internal enum MachineWorkflowStage
{
    BeforeWorkSearch = 0,
    AfterWorkSearch = 1
}
