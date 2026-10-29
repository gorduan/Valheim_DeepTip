using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeepTip.Core;

/// <summary>
/// Minimal JSON reader for glossary files (Valheim ships no JSON library, and DeepTip must not depend on another mod).
/// Objects become <see cref="Dictionary{TKey,TValue}"/> (insertion order kept via <see cref="JsonObject"/>), arrays
/// <see cref="List{T}"/>, numbers double, plus string, bool and null.
/// </summary>
public static class Json
{
	/// <summary>Object with keys in file order (entry order matters for the lexicon and for duplicate detection).</summary>
	public sealed class JsonObject : Dictionary<string, object?>
	{
		public readonly List<string> Order = new();

		public new void Add(string key, object? value)
		{
			if (!ContainsKey(key))
			{
				Order.Add(key);
			}
			this[key] = value;
		}
	}

	public static object? Parse(string text)
	{
		Reader r = new(text);
		r.SkipWhite();
		object? value = r.Value();
		r.SkipWhite();
		if (!r.End)
		{
			throw r.Error("unexpected text after the JSON value");
		}
		return value;
	}

	private sealed class Reader
	{
		private readonly string s;
		private int i;

		public Reader(string text)
		{
			s = text;
			if (s.Length > 0 && s[0] == '﻿')
			{
				i = 1; // BOM
			}
		}

		public bool End => i >= s.Length;

		public FormatException Error(string message)
		{
			int line = 1;
			int col = 1;
			for (int k = 0; k < i && k < s.Length; k++)
			{
				if (s[k] == '\n')
				{
					line++;
					col = 1;
				}
				else
				{
					col++;
				}
			}
			return new FormatException($"{message} (line {line}, column {col})");
		}

		public void SkipWhite()
		{
			while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
			{
				i++;
			}
		}

		public object? Value()
		{
			if (End)
			{
				throw Error("unexpected end");
			}
			char c = s[i];
			switch (c)
			{
				case '{':
					return Object();
				case '[':
					return Array();
				case '"':
					return String();
				case 't':
					Word("true");
					return true;
				case 'f':
					Word("false");
					return false;
				case 'n':
					Word("null");
					return null;
				default:
					if (c == '-' || (c >= '0' && c <= '9'))
					{
						return Number();
					}
					throw Error($"unexpected character '{c}'");
			}
		}

		private void Word(string word)
		{
			if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
			{
				throw Error($"expected {word}");
			}
			i += word.Length;
		}

		private JsonObject Object()
		{
			JsonObject obj = new();
			i++; // {
			SkipWhite();
			if (i < s.Length && s[i] == '}')
			{
				i++;
				return obj;
			}
			while (true)
			{
				SkipWhite();
				if (End || s[i] != '"')
				{
					throw Error("expected a key in quotes");
				}
				string key = String();
				SkipWhite();
				if (End || s[i] != ':')
				{
					throw Error("expected ':'");
				}
				i++;
				SkipWhite();
				obj.Add(key, Value());
				SkipWhite();
				if (End)
				{
					throw Error("unexpected end in an object");
				}
				if (s[i] == ',')
				{
					i++;
					continue;
				}
				if (s[i] == '}')
				{
					i++;
					return obj;
				}
				throw Error("expected ',' or '}'");
			}
		}

		private List<object?> Array()
		{
			List<object?> list = new();
			i++; // [
			SkipWhite();
			if (i < s.Length && s[i] == ']')
			{
				i++;
				return list;
			}
			while (true)
			{
				SkipWhite();
				list.Add(Value());
				SkipWhite();
				if (End)
				{
					throw Error("unexpected end in an array");
				}
				if (s[i] == ',')
				{
					i++;
					continue;
				}
				if (s[i] == ']')
				{
					i++;
					return list;
				}
				throw Error("expected ',' or ']'");
			}
		}

		private string String()
		{
			i++; // "
			StringBuilder sb = new();
			while (true)
			{
				if (End)
				{
					throw Error("unterminated string");
				}
				char c = s[i++];
				if (c == '"')
				{
					return sb.ToString();
				}
				if (c != '\\')
				{
					sb.Append(c);
					continue;
				}
				if (End)
				{
					throw Error("unterminated escape");
				}
				char e = s[i++];
				switch (e)
				{
					case '"': sb.Append('"'); break;
					case '\\': sb.Append('\\'); break;
					case '/': sb.Append('/'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'n': sb.Append('\n'); break;
					case 'r': sb.Append('\r'); break;
					case 't': sb.Append('\t'); break;
					case 'u':
						if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
						{
							throw Error("bad \\u escape");
						}
						sb.Append((char)code);
						i += 4;
						break;
					default:
						throw Error($"bad escape \\{e}");
				}
			}
		}

		private double Number()
		{
			int start = i;
			if (s[i] == '-')
			{
				i++;
			}
			while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
			{
				i++;
			}
			if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
			{
				throw Error("bad number");
			}
			return value;
		}
	}
}
