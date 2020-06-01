using NoJsonTextMustache;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static NoJsonTextMustache.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace NoJsonTextMustache.Tests {
    [TestClass()]
    public class MustacheTests {

        const string t1 = @"
Hello {{name}}
You have just won {{value}} dollars!
{{#in_ca}}
Well, {{taxed_value}} dollars, after taxes.
{{/in_ca}}
";
        const string h1 = @"
{
  ""name"": ""Chris"",
  ""value"": 10000,
  ""taxed_value"": 5000,
  ""in_ca"": true
}
";
        const string r1 = @"
Hello Chris
You have just won 10000 dollars!

Well, 5000 dollars, after taxes.

";
        const string t2 = @"
* {{name}}
* {{age}}
* {{company}}
* {{{company}}}
";
        const string h2 =  @"
{
  ""name"": ""Chris"",
  ""company"": ""<b>GitHub</b>""
}
";
        const string r2 = @"
* Chris
* 
* &lt;b&gt;GitHub&lt;/b&gt;
* <b>GitHub</b>
";
        const string t3 = @"
Shown
{{#person}}
  Never shown!
{{/person}}
";
        const string h3 = @"
{
  ""person"": false
}
";
        const string r3 = @"
Shown

";
        const string t4 = @"
{{#repo}}
  <b>{{name}}</b>
{{/repo}}
";
        const string h4 = @"
{
  ""repo"": [
    { ""name"": ""resque"" },
    { ""name"": ""hub"" },
    { ""name"": ""rip"" }
  ]
}
";
        const string r4 = @"

  <b>resque</b>

  <b>hub</b>

  <b>rip</b>

";
        const string t5 = @"
{{#repo}}
  <b>{{name}}</b>
    {{#nested}}
        NestedName: {{name}}
    {{/nested}}
{{/repo}}
";
        const string h5 = @"
{
  ""repo"": [
    { ""name"": ""resque"", ""nested"":[{""name"":""nestedResque""}] },
    { ""name"": ""hub"" },
    { ""name"": ""rip"" }
  ]
}
";
        const string r5 = @"

  <b>resque</b>
    
        NestedName: nestedResque
    

  <b>hub</b>
    

  <b>rip</b>
    

";



        static string TokensToString(IEnumerable<Token> tokens) {
            return string.Join("", tokens.Select(t => t.ToString()));
        }

        [DataTestMethod]
        [DataRow("afdfadfa{{name}}fdafdafa")]
        [DataRow("{{lastName}}afdfadfa{{naame}}fdafdafa")]
        [DataRow("{{#lastName}}afdfadfa{{/lastName}}fdafdafa")]
        [DataRow("{{#lastName}} afdfadfa {{/lastName}} fdafdafa")]
        public void CanReplicateTemplate(string template) {
            var tokens = Tokenize(template);
            var actual = TokensToString(tokens);
            Assert.AreEqual(template, actual);
        }

        const string context = @"
{
    ""name"": ""Bob"",
    ""lastName"": ""Hope"",
    ""section"": [""Test1"", ""Test2"", ""Second""],
}
";
        [DataTestMethod]
        [DataRow("afdfadfa{{name}}fdafdafa", 3)]
        [DataRow("{{lastName}}afdfadfa{{naame}}fdafdafa", 4)]
        [DataRow("{{#section}}afdfadfa{{/section}}fdafdafa", 2)]
        [DataRow("bbbbbb {{#section}} afdfadfa {{/section}} fdafdafa", 3)]
        [DataRow("bbbbbb {{#section}} afd{{#Second}} fafad {{/Second}} fadfa {{/section}} fdafdafa", 3)]
        public void ParseTest(string template, int topNodesNumber) {
            var tokens = Tokenize(template);
            var tree = Parse(tokens);
            Assert.AreEqual(topNodesNumber, tree.Nodes.Count());
        }

        [DataTestMethod]
        [DataRow(t1, h1, r1)]
        [DataRow(t2, h2, r2)]
        [DataRow(t3, h3, r3)]
        [DataRow(t4, h4, r4)]
        [DataRow(t5, h5, r5)]
        public void RenderTest(string template, string hash, string expected) {

            var tokens = Tokenize(template);
            var tree = Parse(tokens);
            var rendered = Render(tree, hash);
            Assert.AreEqual(expected, rendered);
        }
    }
}