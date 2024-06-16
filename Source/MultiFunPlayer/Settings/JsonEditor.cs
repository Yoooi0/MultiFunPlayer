using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal class JsonEditor
{
    protected virtual void Log(LogLevel level, string message, params object[] args) { }

    public bool RemoveToken(JToken token)
    {
        var path = token.Path;

        try
        {
            token.Remove();
            Log(LogLevel.Info, "Removed token \"{0}\" from \"{1}\"", token.ToString(), path);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to remove token \"{0}\": {1}", path, e.Message);
            return false;
        }
    }

    public bool AddTokenToContainer(JToken token, JContainer container)
    {
        try
        {
            container.Add(token);
            Log(LogLevel.Info, "Added token \"{0}\" to container \"{1}\"", token.ToString(), container.Path);

            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to add token \"{0}\" to container \"{1}\": {2}", token.ToString(), container.Path, e.Message);
            return false;
        }
    }

    public bool InsertTokenToArray(JToken token, int index, JArray array)
    {
        try
        {
            array.Insert(index, token);
            Log(LogLevel.Info, "Inserted token \"{0}\" to array \"{1}\" at index \"{2}\"", token.ToString(), array.Path, index);

            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to insert token \"{0}\" to array \"{1}\" at index \"{2}\": {3}", token.ToString(), array.Path, index, e.Message);
            return false;
        }
    }

    public bool RemoveProperty(JProperty property)
    {
        var path = property.Path;

        try
        {
            property.Remove();
            Log(LogLevel.Info, "Removed property \"{0}\"", path);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to remove property \"{0}\": {1}", path, e.Message);
            return false;
        }
    }

    public bool AddProperty(JObject o, JProperty property)
    {
        try
        {
            o.Add(property);
            Log(LogLevel.Info, "Added property \"{0}={1}\" to \"{2}\"", property.Name, property.Value.ToString(), o.Path);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to add property \"{0}={1}\" to \"{2}\": {3}", property.Name, property.Value.ToString(), o.Path, e.Message);
            return false;
        }
    }

    public bool RenameProperty(ref JProperty property, string newName)
    {
        var parent = property.Parent;
        var value = property.Value;
        var oldPath = property.Path;

        try
        {
            var newProperty = new JProperty(newName, value);
            parent.Add(newProperty);

            property.Value = null;
            property.Remove();

            property = newProperty;

            Log(LogLevel.Info, "Renamed property \"{0}\" to \"{1}\"", oldPath, newName);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to rename property \"{0}\" to \"{1}\": {2}", oldPath, newName, e.Message);
            return false;
        }
    }

    public bool MoveProperty(JProperty property, JObject toObject, bool replace = false)
    {
        var oldParent = property.Parent;
        var oldPath = property.Path;
        var oldProperty = default(JProperty);

        try
        {
            if (replace && toObject.ContainsKey(property.Name))
            {
                oldProperty = toObject.Property(property.Name);
                RemoveProperty(oldProperty);
            }

            property.Remove();
            toObject.Add(property);

            Log(LogLevel.Info, "Moved property \"{0}\" to \"{1}\"", oldPath, property.Path);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to move property \"{0}\" to \"{1}\": {2}", oldPath, toObject.Path, e.Message);
            if (oldProperty != null)
                toObject.Add(oldProperty);
            if (property.Parent == null)
                oldParent.Add(property);

            return false;
        }
    }

    public bool MoveProperty(ref JProperty property, JObject toObject, string newName, bool replace = false)
    {
        var value = property.Value;
        var oldPath = property.Path;
        var oldProperty = default(JProperty);

        try
        {
            var newProperty = new JProperty(newName, value);
            if (replace && toObject.ContainsKey(newName))
            {
                oldProperty = toObject.Property(newName);
                RemoveProperty(oldProperty);
            }

            toObject.Add(newProperty);

            property.Value = null;
            property.Remove();

            property = newProperty;

            Log(LogLevel.Info, "Moved property \"{0}\" to \"{1}\"", oldPath, property.Path);
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to move property \"{0}\" to \"{1}\": {2}", oldPath, toObject.Path, e.Message);
            if (oldProperty != null)
                AddProperty(toObject, oldProperty);

            return false;
        }
    }

    public bool SetProperty(JProperty property, JToken newValue)
    {
        var oldValue = property.Value;
        var path = property.Path;

        try
        {
            property.Value = null;
            property.Value = newValue;

            Log(LogLevel.Info, "Edited property \"{0}\" from \"{1}\" to \"{2}\"", path, oldValue.ToString(), newValue.ToString());
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to edit property \"{0}\": {1}", path, e.Message);
            return false;
        }
    }

    public bool AddPropertyByName(JObject o, string propertyName, JToken token) => AddProperty(o, new JProperty(propertyName, token));
    public bool AddPropertyByName(JObject o, string propertyName, JToken token, out JProperty property)
    {
        property = new JProperty(propertyName, token);
        return AddProperty(o, property);
    }
    public void AddPropertiesByName(JObject o, IEnumerable<KeyValuePair<string, JToken>> propertyNameMap)
    {
        foreach (var (propertyName, token) in propertyNameMap)
            AddPropertyByName(o, propertyName, token);
    }
    public void AddPropertiesByName(JObject o, IEnumerable<KeyValuePair<string, JToken>> propertyNameMap, out IEnumerable<JProperty> properties)
    {
        var result = new List<JProperty>();
        foreach (var (propertyName, token) in propertyNameMap)
            if (AddPropertyByName(o, propertyName, token, out var property))
                result.Add(property);

        properties = result;
    }

    public void RemoveAllProperties(JObject o)
    {
        foreach (var property in o.Properties().ToList())
            RemoveProperty(property);
    }

    public bool RemovePropertyByName(JObject o, string propertyName) => ModifyPropertyByName(o, propertyName, RemoveProperty);
    public bool RenamePropertyByName(JObject o, string propertyName, string newName) => ModifyPropertyByName(o, propertyName, p => RenameProperty(ref p, newName));
    public bool MovePropertyByName(JObject o, string propertyName, JObject toObject, bool replace = false) => ModifyPropertyByName(o, propertyName, p => MoveProperty(p, toObject, replace));
    public bool MovePropertyByName(JObject o, string propertyName, JObject toObject, string newName, bool replace = false) => ModifyPropertyByName(o, propertyName, p => MoveProperty(ref p, toObject, newName, replace));
    public bool EditPropertyByName(JObject o, string propertyName, Func<JToken, JToken> modifier) => ModifyPropertyByName(o, propertyName, p => SetProperty(p, modifier(p.Value)));
    public bool SetPropertyByName(JObject o, string propertyName, JToken value, bool addIfMissing = false)
    {
        if (o.ContainsKey(propertyName))
            return SetProperty(o.Property(propertyName), value);
        else if (addIfMissing)
            return AddPropertyByName(o, propertyName, value);
        return false;
    }

    public bool ModifyPropertyByName(JObject o, string propertyName, Func<JProperty, bool> modifier)
    {
        if (!TryGetProperty(o, propertyName, out var property))
            return false;

        return modifier(property);
    }

    public bool RemovePropertyByName(JObject o, string propertyName, out JProperty property) => ModifyPropertyByName(o, propertyName, RemoveProperty, out property);
    public bool RenamePropertyByName(JObject o, string propertyName, string newName, out JProperty property) => ModifyPropertyByName(o, propertyName, p => RenameProperty(ref p, newName), out property);
    public bool MovePropertyByName(JObject o, string propertyName, JObject toObject, out JProperty property, bool replace = false) => ModifyPropertyByName(o, propertyName, p => MoveProperty(p, toObject, replace), out property);
    public bool MovePropertyByName(JObject o, string propertyName, JObject toObject, string newName, out JProperty property, bool replace = false) => ModifyPropertyByName(o, propertyName, p => MoveProperty(ref p, toObject, newName, replace), out property);

    public bool ModifyPropertyByName(JObject o, string propertyName, Func<JProperty, bool> modifier, out JProperty property)
    {
        if (!TryGetProperty(o, propertyName, out property))
            return false;

        return modifier(property);
    }

    public void RemovePropertiesByName(JObject o, IEnumerable<string> propertyNames) => ModifyPropertiesByName(o, propertyNames, RemoveProperty);
    public void MovePropertiesByName(JObject o, IEnumerable<string> propertyNames, JObject toObject) => ModifyPropertiesByName(o, propertyNames, p => MoveProperty(p, toObject));
    public void EditPropertiesByName(JObject o, IEnumerable<string> propertyNames, Func<JToken, JToken> modifier) => ModifyPropertiesByName(o, propertyNames, p => SetProperty(p, modifier(p.Value)));
    public void SetPropertiesByName(JObject o, IEnumerable<string> propertyNames, JToken value, bool addIfMissing = false)
    {
        foreach (var propertyName in propertyNames)
            SetPropertyByName(o, propertyName, value, addIfMissing);
    }

    public void ModifyPropertiesByName(JObject o, IEnumerable<string> propertyNames, Func<JProperty, bool> modifier)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(o, propertyName, out var property))
                continue;

            modifier(property);
        }
    }

    public void RenamePropertiesByName(JObject o, IEnumerable<KeyValuePair<string, string>> propertyNameMap) => ModifyPropertiesByName(o, propertyNameMap, (p, n) => RenameProperty(ref p, n));
    public void MovePropertiesByName(JObject o, IEnumerable<KeyValuePair<string, string>> propertyNameMap, JObject toObject) => ModifyPropertiesByName(o, propertyNameMap, (p, n) => MoveProperty(ref p, toObject, n));
    public void SetPropertiesByName(JObject o, IEnumerable<KeyValuePair<string, JToken>> propertyNameMap, bool addIfMissing = false)
    {
        foreach (var (propertyName, value) in propertyNameMap)
            SetPropertyByName(o, propertyName, value, addIfMissing);
    }

    public void ModifyPropertiesByName<T>(JObject o, IEnumerable<KeyValuePair<string, T>> propertyNameMap, Action<JProperty, T> modifier)
    {
        foreach (var (propertyName, argument) in propertyNameMap)
        {
            if (!TryGetProperty(o, propertyName, out var property))
                continue;

            modifier(property, argument);
        }
    }

    public bool RemovePropertyByPath(JContainer o, string propertyPath) => ModifyPropertyByPath(o, propertyPath, RemoveProperty);
    public bool RenamePropertyByPath(JContainer o, string propertyPath, string newName) => ModifyPropertyByPath(o, propertyPath, p => RenameProperty(ref p, newName));
    public bool MovePropertyByPath(JContainer o, string propertyPath, JObject toObject) => ModifyPropertyByPath(o, propertyPath, p => MoveProperty(p, toObject));
    public bool MovePropertyByPath(JContainer o, string propertyPath, string newName, JObject toObject) => ModifyPropertyByPath(o, propertyPath, p => MoveProperty(ref p, toObject, newName));
    public bool EditPropertyByPath(JContainer o, string propertyPath, Func<JToken, JToken> modifier) => ModifyPropertyByPath(o, propertyPath, p => SetProperty(p, modifier(p.Value)));
    public bool SetPropertyByPath(JContainer o, string propertyPath, JToken value) => ModifyPropertyByPath(o, propertyPath, p => SetProperty(p, value));

    public bool ModifyPropertyByPath(JContainer o, string propertyPath, Func<JProperty, bool> modifier)
    {
        if (!TrySelectProperty(o, propertyPath, out var property))
            return false;

        return modifier(property);
    }

    public void RemovePropertiesByPath(JContainer o, string propertiesPath) => ModifyPropertiesByPath(o, propertiesPath, p => RemoveProperty(p));
    public void RenamePropertiesByPath(JContainer o, string propertiesPath, string newName) => ModifyPropertiesByPath(o, propertiesPath, p => RenameProperty(ref p, newName));
    public void EditPropertiesByPath(JContainer o, string propertiesPath, Func<JToken, JToken> modifier) => ModifyPropertiesByPath(o, propertiesPath, p => SetProperty(p, modifier(p.Value)));
    public void SetPropertiesByPath(JContainer o, string propertiesPath, JToken value) => ModifyPropertiesByPath(o, propertiesPath, p => SetProperty(p, value));

    public void RemovePropertiesByPath(JContainer o, string propertiesPath, Func<JToken, bool> filter) => ModifyPropertiesByPath(o, propertiesPath, p => filter(p.Value), p => RemoveProperty(p));
    public void RenamePropertiesByPath(JContainer o, string propertiesPath, Func<JToken, bool> filter, string newName) => ModifyPropertiesByPath(o, propertiesPath, p => filter(p.Value), p => RenameProperty(ref p, newName));
    public void EditPropertiesByPath(JContainer o, string propertiesPath, Func<JToken, bool> filter, Func<JToken, JToken> modifier) => ModifyPropertiesByPath(o, propertiesPath, p => filter(p.Value), p => SetProperty(p, modifier(p.Value)));
    public void SetPropertiesByPath(JContainer o, string propertiesPath, Func<JToken, bool> filter, JToken value) => ModifyPropertiesByPath(o, propertiesPath, p => filter(p.Value), p => SetProperty(p, value));

    public void ModifyPropertiesByPath(JContainer o, string propertiesPath, Action<JProperty> modifier) => ModifyPropertiesByPath(o, propertiesPath, _ => true, modifier);
    public void ModifyPropertiesByPath(JContainer o, string propertiesPath, Func<JProperty, bool> filter, Action<JProperty> modifier)
    {
        if (TrySelectProperties(o, propertiesPath, out var properties))
            foreach (var property in properties)
                if (filter(property))
                    modifier(property);
    }

    public void RemovePropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, RemoveProperty);
    public void EditPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, Func<JToken, JToken> modifier, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, p => SetProperty(p, modifier(p.Value)));
    public void SetPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, JToken value, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, p => SetProperty(p, value));

    public void RemovePropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, Func<JToken, bool> filter, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, p => filter(p.Value), RemoveProperty);
    public void EditPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, Func<JToken, bool> filter, Func<JToken, JToken> modifier, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, p => filter(p.Value), p => SetProperty(p, modifier(p.Value)));
    public void SetPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, Func<JToken, bool> filter, JToken value, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, p => filter(p.Value), p => SetProperty(p, value));

    public void ModifyPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, bool selectMultiple, Func<JProperty, bool> modifier) => ModifyPropertiesByPaths(o, propertyPaths, selectMultiple, _ => true, modifier);
    public void ModifyPropertiesByPaths(JContainer o, IEnumerable<string> propertyPaths, bool selectMultiple, Func<JProperty, bool> filter, Func<JProperty, bool> modifier)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (selectMultiple)
            {
                if (TrySelectProperties(o, propertyPath, out var properties))
                    foreach (var property in properties)
                        if (filter(property))
                            modifier(property);
            }
            else if (TrySelectProperty(o, propertyPath, out var property))
            {
                if (filter(property))
                    modifier(property);
            }
        }
    }

    public void RenamePropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, string>> propertyPathMap, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, (p, n) => RenameProperty(ref p, n));
    public void EditPropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, Func<JToken, JToken>>> propertyPathMap, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, (p, m) => SetProperty(p, m(p.Value)));
    public void SetPropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, JToken>> propertyPathMap, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, (p, t) => SetProperty(p, t));

    public void RenamePropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, string>> propertyPathMap, Func<JProperty, bool> filter, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, filter, (p, n) => RenameProperty(ref p, n));
    public void EditPropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, Func<JToken, JToken>>> propertyPathMap, Func<JProperty, bool> filter, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, filter, (p, m) => SetProperty(p, m(p.Value)));
    public void SetPropertiesByPaths(JContainer o, IEnumerable<KeyValuePair<string, JToken>> propertyPathMap, Func<JProperty, bool> filter, bool selectMultiple = true) => ModifyPropertiesByPaths(o, propertyPathMap, selectMultiple, filter, (p, t) => SetProperty(p, t));

    public void ModifyPropertiesByPaths<T>(JContainer o, IEnumerable<KeyValuePair<string, T>> propertyPathMap, bool selectMultiple, Action<JProperty, T> modifier) => ModifyPropertiesByPaths<T>(o, propertyPathMap, selectMultiple, _ => true, modifier);
    public void ModifyPropertiesByPaths<T>(JContainer o, IEnumerable<KeyValuePair<string, T>> propertyPathMap, bool selectMultiple, Func<JProperty, bool> filter, Action<JProperty, T> modifier)
    {
        foreach (var (propertyPath, argument) in propertyPathMap)
        {
            if (selectMultiple)
            {
                if (TrySelectProperties(o, propertyPath, out var properties))
                {
                    foreach (var property in properties)
                        if (filter(property))
                            modifier(property, argument);
                }
            }
            else if (TrySelectProperty(o, propertyPath, out var property))
            {
                if (filter(property))
                    modifier(property, argument);
            }
        }
    }

    public IReadOnlyList<T> GetValues<T>(JObject o, IEnumerable<string> propertyNames) where T : JToken => propertyNames.Select(p => GetValue<T>(o, p)).ToList();
    public T GetValue<T>(JObject o, string propertyName) where T : JToken => TryGetValue<T>(o, propertyName, out var value) ? value : default;
    public bool TryGetValue<T>(JObject o, string propertyName, out T value) where T : JToken
    {
        value = default;
        if (!o.ContainsKey(propertyName))
        {
            Log(LogLevel.Warn, "Failed to find property \"{0}\" in \"{1}\" object", propertyName, o.Path);
            return false;
        }

        var propertyValue = o[propertyName];
        if (propertyValue is not T)
        {
            Log(LogLevel.Warn, "Failed to get property \"{0}\" value as \"{1}\": Propert value is \"{2}\"", propertyValue.Path, typeof(T).Name, propertyValue.GetType().Name);
            return false;
        }

        value = propertyValue as T;
        return true;
    }

    public JToken SelectToken(JToken o, string tokenPath) => TrySelectToken<JToken>(o, tokenPath, out var result) ? result : null;
    public JObject SelectObject(JToken o, string objectPath) => TrySelectToken<JObject>(o, objectPath, out var result) ? result : null;
    public JValue SelectValue(JToken o, string valuePath) => TrySelectToken<JValue>(o, valuePath, out var result) ? result : null;
    public JArray SelectArray(JToken o, string arrayPath) => TrySelectToken<JArray>(o, arrayPath, out var result) ? result : null;

    public IReadOnlyList<JToken> SelectTokens(JToken o, string tokensPath) => TrySelectTokens<JToken>(o, tokensPath, out var result) ? result : [];
    public IReadOnlyList<JObject> SelectObjects(JToken o, string objectsPath) => TrySelectTokens<JObject>(o, objectsPath, out var result) ? result : [];
    public IReadOnlyList<JValue> SelectValues(JToken o, string valuesPath) => TrySelectTokens<JValue>(o, valuesPath, out var result) ? result : [];
    public IReadOnlyList<JArray> SelectArrays(JToken o, string arraysPath) => TrySelectTokens<JArray>(o, arraysPath, out var result) ? result : [];

    public bool TrySelectToken(JToken o, string tokenPath, out JToken result) => TrySelectToken<JToken>(o, tokenPath, out result);
    public bool TrySelectObject(JToken o, string objectPath, out JObject result) => TrySelectToken<JObject>(o, objectPath, out result);
    public bool TrySelectValue(JToken o, string valuePath, out JValue result) => TrySelectToken<JValue>(o, valuePath, out result);
    public bool TrySelectArray(JToken o, string arrayPath, out JArray result) => TrySelectToken<JArray>(o, arrayPath, out result);

    public bool TrySelectTokens(JToken o, string tokensPath, out IReadOnlyList<JToken> result) => TrySelectTokens<JToken>(o, tokensPath, out result);
    public bool TrySelectObjects(JToken o, string objectsPath, out IReadOnlyList<JObject> result) => TrySelectTokens<JObject>(o, objectsPath, out result);
    public bool TrySelectValues(JToken o, string valuesPath, out IReadOnlyList<JValue> result) => TrySelectTokens<JValue>(o, valuesPath, out result);
    public bool TrySelectArrays(JToken o, string arraysPath, out IReadOnlyList<JArray> result) => TrySelectTokens<JArray>(o, arraysPath, out result);

    public bool TrySelectToken<T>(JToken o, string path, out T token) where T : JToken
    {
        token = default;

        try
        {
            var selectedToken = o.SelectToken(path, errorWhenNoMatch: true);
            if (selectedToken == null)
            {
                Log(LogLevel.Warn, "Failed to select \"{0}\" token with path \"{1}\" from \"{2}\" token", typeof(T).Name, path, o.Path);
                return false;
            }

            if (selectedToken is not T)
            {
                Log(LogLevel.Warn, "Failed to select \"{0}\" token with path \"{1}\" from \"{2}\" token: Selected token is \"{3}\"", typeof(T).Name, path, o.Path, selectedToken.GetType().Name);
                return false;
            }

            token = selectedToken as T;
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to select \"{0}\" token with path \"{1}\" from \"{2}\" token: {3}", path, path, o.Path, e.Message);
            return false;
        }
    }

    public bool TrySelectTokens<T>(JToken o, string path, out IReadOnlyList<T> tokens) where T : JToken
    {
        tokens = default;

        try
        {
            tokens = o.SelectTokens(path, errorWhenNoMatch: true).Cast<T>().ToList();
            return true;
        }
        catch (Exception e)
        {
            Log(LogLevel.Warn, "Failed to select \"{0}\" tokens with path \"{1}\" from \"{2}\" token: {3}", typeof(T).Name, path, o.Path, e.Message);
            return false;
        }
    }

    public IReadOnlyList<JProperty> GetProperties(JObject o, IEnumerable<string> propertyNames) => propertyNames.Select(p => GetProperty(o, p)).ToList();
    public JProperty GetProperty(JObject o, string propertyName) => TryGetProperty(o, propertyName, out var property) ? property : null;
    public bool TryGetProperty(JObject o, string propertyName, out JProperty property)
    {
        property = default;
        if (!o.ContainsKey(propertyName))
        {
            Log(LogLevel.Warn, "Object \"{0}\" is missing property \"{1}\"", o.Path, propertyName);
            return false;
        }

        property = o.Property(propertyName);
        return true;
    }

    public JProperty SelectProperty(JContainer o, string propertyPath) => TrySelectProperty(o, propertyPath, out var property) ? property : null;
    public bool TrySelectProperty(JContainer o, string propertyPath, out JProperty result)
    {
        result = default;
        if (TrySelectToken(o, propertyPath, out var valueToken))
            result = valueToken.Parent as JProperty;

        return result != null;
    }

    public IReadOnlyList<JProperty> SelectProperties(JContainer o, string propertiesPath) => TrySelectProperties(o, propertiesPath, out var properties) ? properties : [];
    public bool TrySelectProperties(JContainer o, string propertiesPath, out IReadOnlyList<JProperty> result)
    {
        result = default;
        if (!TrySelectTokens(o, propertiesPath, out var valueTokens))
            return false;

        result = valueTokens.Select(t => t.Parent as JProperty).NotNull().ToList();
        return true;
    }

    public JObject CreateChildObjects(JObject o, params string[] propertyNames)
    {
        bool hasNext;
        var it = ((IEnumerable<string>)propertyNames).GetEnumerator();
        while ((hasNext = it.MoveNext()) && o.ContainsKey(it.Current))
            o = GetValue<JObject>(o, it.Current);

        if (!hasNext)
            return o;

        do
        {
            var child = new JObject();
            AddProperty(o, new JProperty(it.Current, child));
            o = child;
        }
        while (it.MoveNext());

        return o;
    }
}