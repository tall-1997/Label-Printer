using System;
using System.Collections.Generic;

namespace BarTenderPrinter
{
    public class DataSourceItem
    {
        public string Name { get; set; }
        public string Field { get; set; }
        public bool Enabled { get; set; }
        public bool AutoIncrement { get; set; }
        public int AutoStep { get; set; } = 1;
        public bool IsLocked { get; set; }
        public bool LockAfterInput { get; set; }
        public string LockedValue { get; set; } = "";
        public bool AutoIncrementLocked { get; set; }
        public int ExpectedLength { get; set; }
        public long LengthRevision { get; set; }
        public bool LengthEdited { get; set; }
        public bool UseLocalDataValidation { get; set; }

        public DataSourceItem Clone()
        {
            return new DataSourceItem
            {
                Name = Name,
                Field = Field,
                Enabled = Enabled,
                AutoIncrement = AutoIncrement,
                AutoStep = AutoStep,
                IsLocked = IsLocked,
                LockAfterInput = LockAfterInput,
                LockedValue = LockedValue,
                AutoIncrementLocked = AutoIncrementLocked,
                ExpectedLength = ExpectedLength,
                LengthRevision = LengthRevision,
                LengthEdited = LengthEdited,
                UseLocalDataValidation = UseLocalDataValidation
            };
        }
    }

    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftEnd = leftIndex;
                    var rightEnd = rightIndex;
                    while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                    while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;

                    var leftSignificant = leftIndex;
                    var rightSignificant = rightIndex;
                    while (leftSignificant < leftEnd - 1 && left[leftSignificant] == '0') leftSignificant++;
                    while (rightSignificant < rightEnd - 1 && right[rightSignificant] == '0') rightSignificant++;

                    var leftDigits = leftEnd - leftSignificant;
                    var rightDigits = rightEnd - rightSignificant;
                    if (leftDigits != rightDigits) return leftDigits.CompareTo(rightDigits);

                    for (var index = 0; index < leftDigits; index++)
                    {
                        var comparison = left[leftSignificant + index].CompareTo(right[rightSignificant + index]);
                        if (comparison != 0) return comparison;
                    }

                    var runLengthComparison = (leftEnd - leftIndex).CompareTo(rightEnd - rightIndex);
                    if (runLengthComparison != 0) return runLengthComparison;
                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0) return characterComparison;
                leftIndex++;
                rightIndex++;
            }

            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }
    }
}
