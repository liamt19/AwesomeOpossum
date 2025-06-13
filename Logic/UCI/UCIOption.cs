using System.Reflection;

namespace AwesomeOpossum.Logic.UCI
{
    public class UCIOption
    {
        public string Name;
        public string Type;
        public object DefaultValue;
        public object MinValue;
        public object MaxValue;
        public FieldInfo FieldHandle;

        public bool IsString => FieldHandle.FieldType == typeof(string);
        public bool IsBool => FieldHandle.FieldType == typeof(bool);
        public bool IsInt => FieldHandle.FieldType == typeof(int);
        public bool IsFloat => FieldHandle.FieldType == typeof(float);

        public UCIOption(string name, string type, object defaultValue, FieldInfo fieldHandle)
        {
            Name = name;
            Type = type;
            DefaultValue = defaultValue;
            FieldHandle = fieldHandle;

            if (IsInt || IsFloat)
                AutoMinMax();
        }

        public void SetMinMax(object min, object max)
        {
            MinValue = min;
            MaxValue = max;
        }

        public void AutoMinMax()
        {
            if (IsString || IsBool)
            {
                return;
            }

            var d = (float)Convert.ChangeType(DefaultValue, typeof(float));
            if (IsInt)
            {
                MinValue = (int)1;
                MaxValue = (int)float.Round(d * 2);
            }
            else
            {
                MinValue = 0.00001f;
                MaxValue = d * 2.0f;
            }
        }

        public bool HasBadRange()
        {
            if (IsInt)
            {
                return ((int)DefaultValue < (int)MinValue || (int)DefaultValue > (int)MaxValue || (int)MaxValue < (int)MinValue);
            }

            if (IsFloat)
            {
                return ((float)DefaultValue < (float)MinValue || (float)DefaultValue > (float)MaxValue || (float)MaxValue < (float)MinValue);
            }

            return false;
        }


        public string GetSPSAFormat()
        {
            var def = (float)Convert.ChangeType(DefaultValue, typeof(float));
            var min = (float)Convert.ChangeType(MinValue, typeof(float));
            var max = (float)Convert.ChangeType(MaxValue, typeof(float));
            float stepSize = def / 25.0f;

            if (IsInt)
                return $"{FieldHandle.Name}, int, {DefaultValue}, {MinValue}, {MaxValue}, {stepSize}, {0.002f}";
            else
                return $"{FieldHandle.Name}, float, {(float)DefaultValue:F5}, {(float)MinValue:F5}, {(float)MaxValue:F5}, {stepSize:F6}, {0.002f}";

        }

        public override string ToString()
        {
            if (IsBool || IsString)
                return $"option name {Name} type {Type} default {DefaultValue}";

            if (IsInt)
                return $"option name {Name} type {Type} default {DefaultValue} min {MinValue} max {MaxValue}";

            return $"option name {Name} type {Type} default {(float)DefaultValue:F5} min {(float)MinValue:F5} max {(float)MaxValue:F5}";
        }
    }
}
