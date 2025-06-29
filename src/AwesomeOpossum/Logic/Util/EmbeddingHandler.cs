namespace AwesomeOpossum.Logic.Util;

//  https://stackoverflow.com/questions/49522751/how-to-read-get-a-propertygroup-value-from-a-csproj-file-using-c-sharp-in-a-ne
[System.AttributeUsage(System.AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
sealed class ValueFileAttribute(string valueFile) : System.Attribute
{
    public string ValueFile { get; } = valueFile;
}

[System.AttributeUsage(System.AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
sealed class PolicyFileAttribute(string policyFile) : System.Attribute
{
    public string PolicyFile { get; } = policyFile;
}
