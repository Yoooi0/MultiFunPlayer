using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Tests;

public class JsonEditorTests
{
    private readonly JsonEditor _editor = new(LogManager.LogFactory.CreateNullLogger());

    [Fact]
    public void RemoveTokenRemovesPropertyFromObject()
    {
        var o = new JObject() { ["foo"] = 0 };
        var result = _editor.RemoveToken(_editor.SelectProperty(o, "$.foo"));

        Assert.True(result);
        Assert.Empty(o);
    }

    [Fact]
    public void RemoveTokenRemovesTokenFromArray()
    {
        var o = new JArray() { 0, 1 };
        var result = _editor.RemoveToken(_editor.SelectToken(o, "$.[1]"));

        Assert.True(result);
        Assert.Single(o);
        Assert.Equal(0, o[0].ToObject<int>());
    }

    [Fact]
    public void RemovePropertyRemovesProperty()
    {
        var o = new JObject() { ["foo"] = 0, ["bar"] = 1 };
        var result = _editor.RemoveProperty(o.Property("foo"));

        Assert.True(result);
        Assert.Single(o);
        Assert.True(!o.ContainsKey("foo"));
        Assert.True(o.ContainsKey("bar"));
    }

    [Fact]
    public void AddPropertyAddsProperty()
    {
        var o = new JObject();
        var result = _editor.AddProperty(o, new JProperty("foo", 0));

        Assert.True(result);
        Assert.Single(o);
        Assert.True(o.ContainsKey("foo"));
        Assert.Equal(0, o["foo"].Value<int>());
    }

    [Fact]
    public void AddPropertyFailsWhenPropertyAlreadyExists()
    {
        var o = new JObject() { ["foo"] = 0 };
        var result = _editor.AddProperty(o, new JProperty("foo", 1));

        Assert.True(!result);
        Assert.Single(o);
        Assert.True(o.ContainsKey("foo"));
        Assert.Equal(0, o["foo"].Value<int>());
    }

    [Fact]
    public void AddTokenToContainerAddsPropertyToObject()
    {
        var o = new JObject();
        var result = _editor.AddTokenToContainer(new JProperty("foo", 0), o);

        Assert.True(result);
        Assert.Single(o);
        Assert.True(o.ContainsKey("foo"));
        Assert.Equal(0, o["foo"].Value<int>());
    }

    [Fact]
    public void AddTokenToContainerFailsToAddValueToObject()
    {
        var o = new JObject();
        var result = _editor.AddTokenToContainer(new JValue(0), o);

        Assert.True(!result);
        Assert.Empty(o);
    }

    [Fact]
    public void AddTokenToContainerAddsTokenToArray()
    {
        var a = new JArray();
        var result = _editor.AddTokenToContainer(new JValue(0), a);

        Assert.True(result);
        Assert.Single(a);
        Assert.Equal(0, a[0].ToObject<int>());
    }

    [Fact]
    public void AddTokenToContainerFailsToAddPropertyToArray()
    {
        var a = new JArray();
        var result = _editor.AddTokenToContainer(new JProperty("foo", 0), a);

        Assert.True(!result);
        Assert.Empty(a);
    }

    [Fact]
    public void InsertTokenToArrayAddsToken()
    {
        var a = new JArray();
        var result = _editor.InsertTokenToArray("foo", 0, a);

        Assert.True(result);
        Assert.Single(a);
        Assert.Equal("foo", a[0].ToString());
    }

    [Fact]
    public void InsertTokenToArrayFailsWhenIndexOutOfRange()
    {
        var a = new JArray();
        var result = _editor.InsertTokenToArray("foo", 1, a);

        Assert.True(!result);
        Assert.Empty(a);
    }

    [Fact]
    public void RenamePropertyRenamesProperty()
    {
        var o = new JObject() { ["foo"] = 0 };
        var property = o.Property("foo");
        var result = _editor.RenameProperty(ref property, "bar");

        Assert.True(result);
        Assert.Single(o);
        Assert.True(!o.ContainsKey("foo"));
        Assert.True(o.ContainsKey("bar"));
        Assert.True(o["bar"].Value<int>() == 0);
        Assert.Equal("bar", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void RenamePropertyFailsWhenPropertyAlreadyExists()
    {
        var o = new JObject() { ["foo"] = 0, ["bar"] = 1 };
        var property = o.Property("foo");
        var result = _editor.RenameProperty(ref property, "bar");

        Assert.True(!result);
        Assert.Equal(2, o.Count);
        Assert.True(o.ContainsKey("foo"));
        Assert.True(o.ContainsKey("bar"));
        Assert.Equal(0, o["foo"].Value<int>());
        Assert.Equal(1, o["bar"].Value<int>());
        Assert.Equal("foo", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void MovePropertyMovesProperty()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject();
        var result = _editor.MoveProperty(from.Property("foo"), to);

        Assert.True(result);
        Assert.Empty(from);
        Assert.Single(to);
        Assert.True(to.ContainsKey("foo"));
        Assert.True(to["foo"].Value<int>() == 0);
    }

    [Fact]
    public void MovePropertyFailsWhenPropertyAlreadyExists()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject() { ["foo"] = 1 };
        var result = _editor.MoveProperty(from.Property("foo"), to);

        Assert.True(!result);
        Assert.Single(from);
        Assert.Single(to);
        Assert.True(from.ContainsKey("foo"));
        Assert.True(to.ContainsKey("foo"));
        Assert.Equal(0, from["foo"].Value<int>());
        Assert.Equal(1, to["foo"].Value<int>());
    }

    [Fact]
    public void MovePropertyOverwritesWhenPropertyAlreadyExists()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject() { ["foo"] = 1 };
        var result = _editor.MoveProperty(from.Property("foo"), to, replace: true);

        Assert.True(result);
        Assert.Empty(from);
        Assert.Single(to);
        Assert.True(to.ContainsKey("foo"));
        Assert.Equal(0, to["foo"].Value<int>());
    }

    [Fact]
    public void MovePropertyWithNewNameMovesAndRenamesProperty()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject();
        var property = from.Property("foo");
        var result = _editor.MoveProperty(ref property, to, "bar");

        Assert.True(result);
        Assert.Empty(from);
        Assert.Single(to);
        Assert.True(to.ContainsKey("bar"));
        Assert.Equal(0, to["bar"].Value<int>());
        Assert.Equal("bar", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void MovePropertyWithNewNameFailsWhenPropertyAlreadyExists()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject() { ["bar"] = 1 };
        var property = from.Property("foo");
        var result = _editor.MoveProperty(ref property, to, "bar");

        Assert.True(!result);
        Assert.Single(from);
        Assert.Single(to);
        Assert.True(from.ContainsKey("foo"));
        Assert.Equal(0, from["foo"].Value<int>());
        Assert.True(to.ContainsKey("bar"));
        Assert.Equal(1, to["bar"].Value<int>());
        Assert.Equal("foo", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void MovePropertyWithNewNameOverwritesWhenPropertyAlreadyExists()
    {
        var from = new JObject() { ["foo"] = 0 };
        var to = new JObject() { ["bar"] = 1 };
        var property = from.Property("foo");
        var result = _editor.MoveProperty(ref property, to, "bar", replace: true);

        Assert.True(result);
        Assert.Empty(from);
        Assert.Single(to);
        Assert.True(to.ContainsKey("bar"));
        Assert.Equal(0, to["bar"].Value<int>());
        Assert.Equal("bar", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void SetPropertySetsProperty()
    {
        var o = new JObject() { ["foo"] = 0 };
        var result = _editor.SetProperty(o.Property("foo"), 1);

        Assert.True(result);
        Assert.Single(o);
        Assert.Single(o);
        Assert.True(o.ContainsKey("foo"));
        Assert.Equal(1, o["foo"].Value<int>());
    }

    [Fact]
    public void GetPropertyGetsProperty()
    {
        var o = new JObject() { ["foo"] = 0 };
        var property = _editor.GetProperty(o, "foo");

        Assert.NotNull(property);
        Assert.Equal("foo", property.Name);
        Assert.Equal(0, property.Value.ToObject<int>());
    }

    [Fact]
    public void GetPropertyFailsWhenPropertyMissing()
    {
        var o = new JObject() { ["foo"] = 0 };
        var property = _editor.GetProperty(o, "bar");

        Assert.True(property == null);
    }

    [Fact]
    public void GetValueGetsValue()
    {
        var o = new JObject() { ["foo"] = 0 };
        var value = _editor.GetValue<JValue>(o, "foo");

        Assert.NotNull(value);
        Assert.Equal(0, value.ToObject<int>());
    }

    [Fact]
    public void GetValueFailsWhenPropertyMissing()
    {
        var o = new JObject();
        var value = _editor.GetValue<JValue>(o, "foo");

        Assert.Null(value);
    }

    [Theory]
    [InlineData("$.property", typeof(JValue))]
    [InlineData("$.array", typeof(JArray))]
    [InlineData("$.object", typeof(JObject))]
    public void SelectTokenSelectsToken(string path, Type type)
    {
        var o = new JObject()
        {
            ["property"] = 0,
            ["array"] = new JArray(),
            ["object"] = new JObject()
        };

        var value = _editor.SelectToken(o, path);

        Assert.NotNull(value);
        Assert.Equal(type, value.GetType());
    }

    [Fact]
    public void SelectTokenFailsWhenFoundMultiple()
    {
        var o = new JObject()
        {
            ["array"] = new JArray()
            {
                new JObject() { ["foo"] = 0 },
                new JObject() { ["foo"] = 1 }
            }
        };

        var value = _editor.SelectToken(o, "$.array[*].foo");

        Assert.Null(value);
    }

    [Fact]
    public void SelectTokenFailsWhenNotFound()
    {
        var o = new JObject();
        var value = _editor.SelectToken(o, "$.foo");

        Assert.Null(value);
    }

    [Theory]
    [InlineData("$.property")]
    [InlineData("$.array")]
    [InlineData("$.object")]
    public void SelectPropertySelectsProperty(string path)
    {
        var o = new JObject()
        {
            ["property"] = 0,
            ["array"] = new JArray(),
            ["object"] = new JObject()
        };

        var value = _editor.SelectProperty(o, path);

        Assert.NotNull(value);
    }

    [Fact]
    public void SelectPropertyFailsWhenFoundMultiple()
    {
        var o = new JObject()
        {
            ["array"] = new JArray()
            {
                new JObject() { ["foo"] = 0 },
                new JObject() { ["foo"] = 1 }
            }
        };

        var value = _editor.SelectProperty(o, "$.array[*].foo");

        Assert.Null(value);
    }

    [Fact]
    public void SelectPropertyFailsWhenNotFound()
    {
        var o = new JObject();
        var value = _editor.SelectProperty(o, "$.foo");

        Assert.Null(value);
    }

    [Theory]
    [InlineData("$.foo[*].property", typeof(JValue))]
    [InlineData("$.foo[*].array", typeof(JArray))]
    [InlineData("$.foo[*].object", typeof(JObject))]
    public void SelectTokensSelectsTokens(string path, Type type)
    {
        var x = new JObject()
        {
            ["property"] = 0,
            ["array"] = new JArray(),
            ["object"] = new JObject()
        };
        var o = new JObject()
        {
            ["foo"] = new JArray() { x, x, x }
        };

        var values = _editor.SelectTokens(o, path);

        Assert.Equal(3, values.Count);
        Assert.All(values, v => Assert.Equal(type, v.GetType()));
    }

    [Fact]
    public void SelectTokensFailsWhenNotFound()
    {
        var o = new JObject();
        var values = _editor.SelectTokens(o, "$.foo[*]");

        Assert.Empty(values);
    }

    [Theory]
    [InlineData("$.foo[*].property")]
    [InlineData("$.foo[*].array")]
    [InlineData("$.foo[*].object")]
    public void SelectPropertiesSelectsProperties(string path)
    {
        var x = new JObject()
        {
            ["property"] = 0,
            ["array"] = new JArray(),
            ["object"] = new JObject()
        };
        var o = new JObject()
        {
            ["foo"] = new JArray() { x, x, x }
        };

        var values = _editor.SelectProperties(o, path);

        Assert.Equal(3, values.Count);
    }

    [Fact]
    public void SelectPropertiesFailsWhenNotFound()
    {
        var o = new JObject();
        var values = _editor.SelectProperties(o, "$.foo[*]");

        Assert.Empty(values);
    }

    private void AssertCreateChildObjects(JObject o, JObject result)
    {
        Assert.True(o.ContainsKey("foo"));
        Assert.Single(o);

        var foo = o["foo"] as JObject;
        Assert.NotNull(foo);
        Assert.True(foo.ContainsKey("bar"));
        Assert.Single(foo);

        var bar = foo["bar"] as JObject;
        Assert.NotNull(bar);
        Assert.True(bar.ContainsKey("baz"));
        Assert.Single(bar);

        var baz = bar["baz"] as JObject;
        Assert.NotNull(baz);

        Assert.Equal(result, baz);
    }

    [Fact]
    public void CreateChildObjectsCreatesNewObjects()
    {
        var o = new JObject();
        var result = _editor.CreateChildObjects(o, "foo", "bar", "baz");

        AssertCreateChildObjects(o, result);
    }

    [Fact]
    public void CreateChildObjectsCreatesNewObjectsWithAlreadyExistingObjects()
    {
        var o = new JObject() { ["foo"] = new JObject() { ["bar"] = new JObject() }};
        var result = _editor.CreateChildObjects(o, "foo", "bar", "baz");

        AssertCreateChildObjects(o, result);
    }

    [Fact]
    public void CreateChildObjectsReturnsLastObjectWhenAllObjectsExist()
    {
        var o = new JObject() { ["foo"] = new JObject() { ["bar"] = new JObject() { ["baz"] = new JObject() } } };
        var result = _editor.CreateChildObjects(o, "foo", "bar", "baz");

        AssertCreateChildObjects(o, result);
    }
}