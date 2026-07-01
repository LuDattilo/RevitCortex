using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    public sealed class DynamoValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }

        private DynamoValidationResult(bool isValid, IReadOnlyList<string> errors)
        {
            IsValid = isValid;
            Errors = errors;
        }

        public static DynamoValidationResult Ok()
            => new DynamoValidationResult(true, new List<string>());

        public static DynamoValidationResult Fail(params string[] errors)
            => new DynamoValidationResult(false, new List<string>(errors));
    }
}
