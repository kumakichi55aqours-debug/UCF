using System;

public class SkillExecutionCancelledException : Exception
{
    public SkillExecutionCancelledException()
    {
    }

    public SkillExecutionCancelledException(string message) : base(message)
    {
    }
}

