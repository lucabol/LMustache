using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

#nullable enable

// Loosely based on https://github.com/wildbit/mustachio

#pragma warning disable CA1303 // Do not pass literals as localized parameters

namespace Mustache {

    public enum TokenType {
        Content,
        Comment,
        CollectionOpen,
        CollectionClose,
        InvertedElement,
        EscapedElement,
        UnEscapedElement
    }

    public class Token {
        public TokenType Type { get; set; }
        public string Value { get; set; } = "";

        // Doesn't preserve spaces and exact char used for unescaped element
        public override string ToString() {
            var s = Type switch {
                TokenType.Content => Value,
                TokenType.Comment => $"{{{{!{Value}}}}}",
                TokenType.CollectionOpen => $"{{{{#{Value}}}}}",
                TokenType.CollectionClose => $"{{{{/{Value}}}}}",
                TokenType.InvertedElement => $"{{{{^{Value}}}}}",
                TokenType.UnEscapedElement => $"{{{{{{{Value}}}}}",
                TokenType.EscapedElement => $"{{{{{Value}}}}}",
                _ => ""
            };
            return s;
        }
    }
    
    public static class Parser {

        private static readonly Regex _tokenFinder = new Regex("([{]{2}[^{}]+?[}]{2})|([{]{3}[^{}]+?[}]{3})",
                    RegexOptions.Compiled | RegexOptions.Compiled);

        public static IEnumerable<Token> Tokenize(string template) {
           
            var matches = _tokenFinder.Matches(template);
            var idx = 0;
            var tokens = new List<Token>();
            foreach (Match? m in matches) {
                if(m == null) throw new Exception("How can a regex match be null?");

                // Capture text that comes before a mustache tag
                if(m.Index > idx)
                    tokens.Add(new Token { Type = TokenType.Content, Value = template.Substring(idx, m.Index - idx)});

                if(m.Value.StartsWith("{{#", StringComparison.InvariantCulture)) {
                    var token = m.Value.TrimStart('{').TrimEnd('}').TrimStart('#').Trim();
                    tokens.Add(new Token { Type = TokenType.CollectionOpen, Value = token });
                } else if(m.Value.StartsWith("{{/", StringComparison.InvariantCulture)) {
                    var token = m.Value.TrimStart('{').TrimEnd('}').TrimStart('/').Trim();
                    tokens.Add(new Token { Type = TokenType.CollectionClose, Value = token });
                } else if(m.Value.StartsWith("{{^", StringComparison.InvariantCulture)) {
                    var token = m.Value.TrimStart('{').TrimEnd('}').TrimStart('^').Trim();
                    tokens.Add(new Token { Type = TokenType.InvertedElement, Value = token });
                } else if (m.Value.StartsWith("{{{", StringComparison.InvariantCulture) | m.Value.StartsWith("{{&", StringComparison.InvariantCulture))
                {
                    var token = m.Value.TrimStart('{').TrimEnd('}').TrimStart('&').Trim();
                    tokens.Add(new Token { Type = TokenType.UnEscapedElement, Value = token });
                } else if(m.Value.StartsWith("{{!", StringComparison.InvariantCulture)) {
                    var token = m.Value.TrimStart('{').TrimEnd('}').TrimStart('!');
                    tokens.Add(new Token { Type = TokenType.Comment, Value = token });
                } else if(m.Value.StartsWith("{{", StringComparison.InvariantCulture)) {
                    var token = m.Value.TrimStart('{').TrimEnd('}');
                    tokens.Add(new Token { Type = TokenType.EscapedElement, Value = token });
                } else {
                    throw new Exception("Unknown mustache tag.");
                }
                idx = m.Index + m.Length;
            }
            if(idx < template.Length)
                tokens.Add(new Token { Type = TokenType.Content, Value = template.Substring(idx)});

            return tokens;
        }
       
        static (Collection, IEnumerator<Token>) HandleContent(Token content, IEnumerator<Token> rest, Collection tree) =>
            (new Collection { Name = tree.Name,
                             Nodes = tree.Nodes.ToImmutableList().Add(new Content { Text = content.Value}) },
            rest);

        static (Collection, IEnumerator<Token>) HandleComment(Token _, IEnumerator<Token> rest, Collection tree) =>
            (tree, rest);


        static (Collection, IEnumerator<Token>) HandleEscapedVariable(Token variable, IEnumerator<Token> rest, Collection tree) =>
            (new Collection { Name = tree.Name,
                             Nodes = tree.Nodes.ToImmutableList().Add(new EscapedVariable { Name = variable.Value }) },
            rest);

        static (Collection, IEnumerator<Token>) HandleUnEscapedVariable(Token variable, IEnumerator<Token> rest, Collection tree) =>
            (new Collection { Name = tree.Name,
                             Nodes = tree.Nodes.ToImmutableList().Add(new UnEscapedVariable { Name = variable.Value }) },
            rest);

        static (Collection, IEnumerator<Token>) HandleOpenCollection(Token open, IEnumerator<Token> rest, Collection tree) {
            var c = open.Value.Length == 0
                ? new Collection { Name = "" }
                : new Collection { Name = open.Value };


            var shouldExit = false;
            var stillTokens = rest.MoveNext();

            while(!shouldExit && stillTokens) {
                var t = rest.Current;
                switch (t.Type) {
                    case TokenType.Content:
                        (c, rest) = HandleContent(t, rest, c);
                        break;
                    case TokenType.Comment:
                        (c, rest) = HandleComment(t, rest, c);
                        break;
                    case TokenType.CollectionOpen:
                        (c, rest) = HandleOpenCollection(t, rest, c);
                        break;
                    case TokenType.CollectionClose:
                        if(t.Value == open.Value) shouldExit = true;
                        break;
                    case TokenType.InvertedElement:
                        break;
                    case TokenType.EscapedElement:
                        (c, rest) = HandleEscapedVariable(t, rest, c);
                        break;
                    case TokenType.UnEscapedElement:
                        (c, rest) = HandleUnEscapedVariable(t, rest, c);
                        break;
                    default:
                        break;
                }
                if(!shouldExit)
                    stillTokens = rest.MoveNext();
            }
            if(open.Value.Length == 0)
                return (c, rest); // top value colleciton
            else
                return (new Collection {Name = tree.Name, Nodes = tree.Nodes.ToImmutableArray().Add(c) }
                      , rest);
        }

        static public Collection Parse(IEnumerable<Token> tokens) {
            if(tokens == null) throw new ArgumentNullException(nameof(tokens));

            var en = tokens.GetEnumerator();
            var c = new Collection();
            var rootToken = new Token { Type = TokenType.CollectionOpen, Value = ""};
            
            var (res, rest) = HandleOpenCollection(rootToken, en, c);
            return res;
        }

        static string FindProperty(string name, ImmutableStack<JsonElement> path) {
            while(!path.IsEmpty) {
                path = path.Pop(out var el);
                if(el.ValueKind == JsonValueKind.Object) { 
                    var found = el.TryGetProperty(name, out var val);
                    if(found) return val.ToString()!;
                }
            }
            return "";
        }
        static StringBuilder RenderNode(Node n, StringBuilder sb, ImmutableStack<JsonElement> path) => n switch
        {
            Content c => sb.Append(c.Text),
            EscapedVariable e => sb.Append(HttpUtility.HtmlEncode(FindProperty(e.Name, path))),
            UnEscapedVariable u => sb.Append(FindProperty(u.Name, path)),
            Collection c => RenderCollection(c, sb, path),
            _ => sb.Append("UNKNOWN NODE")
        };

        static StringBuilder RenderCollection(Collection c, StringBuilder sb, ImmutableStack<JsonElement> path) {
            
            var found = path.Peek().TryGetProperty(c.Name, out var el);
            if(!found) return sb;

            path = path.Push(el);

            // . If false of empty list return sb
            if(el.ValueKind == JsonValueKind.False) return sb;
            if(el.ValueKind == JsonValueKind.Array && el.GetArrayLength() == 0) return sb;
            // . If true render each node in this context
            if(el.ValueKind == JsonValueKind.True ) { // Object for initial collection
                sb = RenderObject(c, sb, path);
            }
            // . Otherwise for each object render node with json object updated to current
            else if(el.ValueKind == JsonValueKind.Array) {
                foreach (var subEl in el.EnumerateArray()) {
                    foreach (var n in c.Nodes) {
                        sb = RenderNode(n, sb, path.Push(subEl));
                    }
                }
            }
            // . We can't be on a node that is not an array or a bool
            else {
                throw new ArgumentException($"{nameof(el)} at {c.Name} is not a bool or array node");
            }
            return sb;
        }

        static StringBuilder RenderObject(Collection c, StringBuilder sb, ImmutableStack<JsonElement> path) {
                foreach (var n in c.Nodes) {
                    sb = RenderNode(n, sb, path);
                }
                return sb;
        }

        public static string Render(Collection c, string jsonText) {
            if(c == null) throw new ArgumentNullException(nameof(c));

            var vars = JsonDocument.Parse(jsonText).RootElement;
            var sb = new StringBuilder();
            sb = RenderObject(c, sb, ImmutableStack.Create<JsonElement>(vars));
            var s = sb.ToString();
            return s;
        }

    }
    public abstract class Node { }
    public class Content: Node { public string Text { get;set;} = ""; }
    public class EscapedVariable: Node { public string Name { get;set;} = ""; }
    public class UnEscapedVariable: Node { public string Name { get;set;} = ""; }
    public class Collection: Node {
        public string Name { get; set;} = "";
        public IEnumerable<Node> Nodes { get; set; } = new List<Node>();
    }

}
