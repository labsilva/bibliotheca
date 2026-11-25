namespace Bibliotheca.Backend;

public static class DictionaryExtensions
{
    public static void AddNonNull<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, 
        TKey key,
        TValue value)
    {
        if(value != null)
        {
            dict.Add(key, value);
        }
    }
}
