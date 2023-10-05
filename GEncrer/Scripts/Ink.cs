using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;

namespace Ink.Runtime
{
	public class StatePatch{
		public Dictionary<string, Runtime.Object> globals { get { return _globals;  } }
		public HashSet<string> changedVariables { get { return _changedVariables;  } }
		public Dictionary<Container, int> visitCounts { get { return _visitCounts;  } }
		public Dictionary<Container, int> turnIndices { get { return _turnIndices;  } }

		public StatePatch(StatePatch toCopy)
		{
			if( toCopy != null ) {
				_globals = new Dictionary<string, Object>(toCopy._globals);
				_changedVariables = new HashSet<string>(toCopy._changedVariables);
				_visitCounts = new Dictionary<Container, int>(toCopy._visitCounts);
				_turnIndices = new Dictionary<Container, int>(toCopy._turnIndices);
			} else {
				_globals = new Dictionary<string, Object>();
				_changedVariables = new HashSet<string>();
				_visitCounts = new Dictionary<Container, int>();
				_turnIndices = new Dictionary<Container, int>();
			}
		}

		public bool TryGetGlobal(string name, out Runtime.Object value)
		{
			return _globals.TryGetValue(name, out value);
		}

		public void SetGlobal(string name, Runtime.Object value){
			_globals[name] = value;
		}

		public void AddChangedVariable(string name)
		{
			_changedVariables.Add(name);
		}

		public bool TryGetVisitCount(Container container, out int count)
		{
			return _visitCounts.TryGetValue(container, out count);
		}

		public void SetVisitCount(Container container, int count)
		{
			_visitCounts[container] = count;
		}

		public void SetTurnIndex(Container container, int index)
		{
			_turnIndices[container] = index;
		}

		public bool TryGetTurnIndex(Container container, out int index)
		{
			return _turnIndices.TryGetValue(container, out index);
		}

		Dictionary<string, Runtime.Object> _globals;
		HashSet<string> _changedVariables = new HashSet<string>();
		Dictionary<Container, int> _visitCounts = new Dictionary<Container, int>();
		Dictionary<Container, int> _turnIndices = new Dictionary<Container, int>();
	}
	public struct Pointer{
		public Container container;
		public int index;

		public Pointer (Container container, int index)
		{
			this.container = container;
			this.index = index;
		}

		public Runtime.Object Resolve ()
		{
			if (index < 0) return container;
			if (container == null) return null;
			if (container.content.Count == 0) return container;
			if (index >= container.content.Count) return null;
			return container.content [index];

		}

		public bool isNull {
			get {
				return container == null;
			}
		}

		public Path path {
			get {
				if( isNull ) return null;

				if (index >= 0)
					return container.path.PathByAppendingComponent (new Path.Component(index));
				else
					return container.path;
			}
		}

		public override string ToString ()
		{
			if (container == null)
				return "Ink Pointer (null)";

			return "Ink Pointer -> " + container.path.ToString () + " -- index " + index;
		}

		public static Pointer StartOf (Container container)
		{
			return new Pointer {
				container = container,
				index = 0
			};
		}

		public static Pointer Null = new Pointer { container = null, index = -1 };
	}
	public static class SimpleJson{
		public static Dictionary<string, object> TextToDictionary (string text)
		{
			return new Reader (text).ToDictionary ();
		}

		public static List<object> TextToArray(string text)
		{
			return new Reader(text).ToArray();
		}

		class Reader
		{
			public Reader (string text)
			{
				_text = text;
				_offset = 0;
				SkipWhitespace ();
				_rootObject = ReadObject ();
			}
			public Dictionary<string, object> ToDictionary ()
			{
				return (Dictionary<string, object>)_rootObject;
			}

			public List<object> ToArray()
			{
				return (List<object>)_rootObject;
			}

			bool IsNumberChar (char c)
			{
				return c >= '0' && c <= '9' || c == '.' || c == '-' || c == '+' || c == 'E' || c == 'e';
			}

			bool IsFirstNumberChar(char c)
			{
				return c >= '0' && c <= '9' || c == '-' || c == '+';
			}
			object ReadObject ()
			{
				var currentChar = _text [_offset];
				if(currentChar == '{' )
					return ReadDictionary ();
				
				else if (currentChar == '[')
					return ReadArray ();

				else if (currentChar == '"')
					return ReadString ();

				else if (IsFirstNumberChar(currentChar))
					return ReadNumber ();

				else if (TryRead ("true"))
					return true;

				else if (TryRead ("false"))
					return false;

				else if (TryRead ("null"))
					return null;

				throw new System.Exception ("Unhandled object type in JSON: "+_text.Substring (_offset, 30));
			}

			Dictionary<string, object> ReadDictionary ()
			{
				var dict = new Dictionary<string, object> ();

				Expect ("{");

				SkipWhitespace ();

				// Empty dictionary?
				if (TryRead ("}"))
					return dict;

				do {

					SkipWhitespace ();

					// Key
					var key = ReadString ();
					Expect (key != null, "dictionary key");

					SkipWhitespace ();

					// :
					Expect (":");

					SkipWhitespace ();

					// Value
					var val = ReadObject ();
					Expect (val != null, "dictionary value");

					// Add to dictionary
					dict [key] = val;

					SkipWhitespace ();

				} while ( TryRead (",") );

				Expect ("}");

				return dict;
			}

			List<object> ReadArray ()
			{
				var list = new List<object> ();

				Expect ("[");

				SkipWhitespace ();

				// Empty list?
				if (TryRead ("]"))
					return list;

				do {

					SkipWhitespace ();

					// Value
					var val = ReadObject ();

					// Add to array
					list.Add (val);

					SkipWhitespace ();

				} while (TryRead (","));

				Expect ("]");

				return list;
			}

			string ReadString ()
			{
				Expect ("\"");

				var sb = new StringBuilder();

				for (; _offset < _text.Length; _offset++) {
					var c = _text [_offset];

					if (c == '\\') {
						// Escaped character
						_offset++;
						if (_offset >= _text.Length) {
							throw new Exception("Unexpected EOF while reading string");
						}
						c = _text[_offset];
						switch (c)
						{
							case '"':
							case '\\':
							case '/': // Yes, JSON allows this to be escaped
								sb.Append(c);
								break;
							case 'n':
								sb.Append('\n');
								break;
							case 't':
								sb.Append('\t');
								break;
							case 'r':
							case 'b':
							case 'f':
								// Ignore other control characters
								break;
							case 'u':
								// 4-digit Unicode
								if (_offset + 4 >=_text.Length) {
									throw new Exception("Unexpected EOF while reading string");
								}
								var digits = _text.Substring(_offset + 1, 4);
								int uchar;
								if (int.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uchar)) {
									sb.Append((char)uchar);
									_offset += 4;
								} else {
									throw new Exception("Invalid Unicode escape character at offset " + (_offset - 1));
								}
								break;
							default:
								// The escaped character is invalid per json spec
								throw new Exception("Invalid Unicode escape character at offset " + (_offset - 1));
						}
					} else if( c == '"' ) {
						break;
					} else {
						sb.Append(c);
					}
				}

				Expect ("\"");
				return sb.ToString();
			}

			object ReadNumber ()
			{
				var startOffset = _offset;

				bool isFloat = false;
				for (; _offset < _text.Length; _offset++) {
					var c = _text [_offset];
					if (c == '.' || c == 'e' || c == 'E') isFloat = true;
					if (IsNumberChar (c))
						continue;
					else
						break;
				}

				string numStr = _text.Substring (startOffset, _offset - startOffset);

				if (isFloat) {
					float f;
					if (float.TryParse (numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) {
						return f;
					}
				} else {
					int i;
					if (int.TryParse (numStr, out i)) {
						return i;
					}
				}

				throw new System.Exception ("Failed to parse number value: "+numStr);
			}

			bool TryRead (string textToRead)
			{
				if (_offset + textToRead.Length > _text.Length)
					return false;
				
				for (int i = 0; i < textToRead.Length; i++) {
					if (textToRead [i] != _text [_offset + i])
						return false;
				}

				_offset += textToRead.Length;

				return true;
			}

			void Expect (string expectedStr)
			{
				if (!TryRead (expectedStr))
					Expect (false, expectedStr);
			}

			void Expect (bool condition, string message = null)
			{
				if (!condition) {
					if (message == null) {
						message = "Unexpected token";
					} else {
						message = "Expected " + message;
					}
					message += " at offset " + _offset;

					throw new System.Exception (message);
				}
			}

			void SkipWhitespace ()
			{
				while (_offset < _text.Length) {
					var c = _text [_offset];
					if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
						_offset++;
					else
						break;
				}
			}

			string _text;
			int _offset;

			object _rootObject;
		}


		public class Writer
		{
			public Writer()
			{
				_writer = new StringWriter();
			}

			public Writer(Stream stream)
			{
				_writer = new System.IO.StreamWriter(stream, Encoding.UTF8);
			}

			public void WriteObject(Action<Writer> inner)
			{
				WriteObjectStart();
				inner(this);
				WriteObjectEnd();
			}

			public void WriteObjectStart()
			{
				StartNewObject(container: true);
				_stateStack.Push(new StateElement { type = State.Object });
				_writer.Write("{");
			}

			public void WriteObjectEnd()
			{
				Assert(state == State.Object);
				_writer.Write("}");
				_stateStack.Pop();
			}

			public void WriteProperty(string name, Action<Writer> inner)
			{
				WriteProperty<string>(name, inner);
			}

			public void WriteProperty(int id, Action<Writer> inner)
			{
				WriteProperty<int>(id, inner);
			}

			public void WriteProperty(string name, string content)
			{
				WritePropertyStart(name);
				Write(content);
				WritePropertyEnd();
			}

			public void WriteProperty(string name, int content)
			{
				WritePropertyStart(name);
				Write(content);
				WritePropertyEnd();
			}

			public void WriteProperty(string name, bool content)
			{
				WritePropertyStart(name);
				Write(content);
				WritePropertyEnd();
			}

			public void WritePropertyStart(string name)
			{
				WritePropertyStart<string>(name);
			}

			public void WritePropertyStart(int id)
			{
				WritePropertyStart<int>(id);
			}

			public void WritePropertyEnd()
			{
				Assert(state == State.Property);
				Assert(childCount == 1);
				_stateStack.Pop();
			}

			public void WritePropertyNameStart()
			{
				Assert(state == State.Object);

				if (childCount > 0)
					_writer.Write(",");

				_writer.Write("\"");

				IncrementChildCount();

				_stateStack.Push(new StateElement { type = State.Property });
				_stateStack.Push(new StateElement { type = State.PropertyName });
			}

			public void WritePropertyNameEnd()
			{
				Assert(state == State.PropertyName);

				_writer.Write("\":");

				// Pop PropertyName, leaving Property state
				_stateStack.Pop();
			}

			public void WritePropertyNameInner(string str)
			{
				Assert(state == State.PropertyName);
				_writer.Write(str);
			}

			void WritePropertyStart<T>(T name)
			{
				Assert(state == State.Object);

				if (childCount > 0)
					_writer.Write(",");

				_writer.Write("\"");
				_writer.Write(name);
				_writer.Write("\":");

				IncrementChildCount();

				_stateStack.Push(new StateElement { type = State.Property });
			}


			// allow name to be string or int
			void WriteProperty<T>(T name, Action<Writer> inner)
			{
				WritePropertyStart(name);

				inner(this);

				WritePropertyEnd();
			}

			public void WriteArrayStart()
			{
				StartNewObject(container: true);
				_stateStack.Push(new StateElement { type = State.Array });
				_writer.Write("[");
			}

			public void WriteArrayEnd()
			{
				Assert(state == State.Array);
				_writer.Write("]");
				_stateStack.Pop();
			}

			public void Write(int i)
			{
				StartNewObject(container: false);
				_writer.Write(i);
			}

			public void Write(float f)
			{
				StartNewObject(container: false);

				// TODO: Find an heap-allocation-free way to do this please!
				// _writer.Write(formatStr, obj (the float)) requires boxing
				// Following implementation seems to work ok but requires creating temporary garbage string.
				string floatStr = f.ToString(System.Globalization.CultureInfo.InvariantCulture);
				if( floatStr == "Infinity" ) {
					_writer.Write("3.4E+38"); // JSON doesn't support, do our best alternative
				} else if (floatStr == "-Infinity") {
					_writer.Write("-3.4E+38"); // JSON doesn't support, do our best alternative
				} else if ( floatStr == "NaN" ) {
					_writer.Write("0.0"); // JSON doesn't support, not much we can do
				} else {
					_writer.Write(floatStr);
					if (!floatStr.Contains(".") && !floatStr.Contains("E")) 
						_writer.Write(".0"); // ensure it gets read back in as a floating point value
				}
			}

			public void Write(string str, bool escape = true)
			{
				StartNewObject(container: false);

				_writer.Write("\"");
				if (escape)
					WriteEscapedString(str);
				else
					_writer.Write(str);
				_writer.Write("\"");
			}

			public void Write(bool b)
			{
				StartNewObject(container: false);
				_writer.Write(b ? "true" : "false");
			}

			public void WriteNull()
			{
				StartNewObject(container: false);
				_writer.Write("null");
			}

			public void WriteStringStart()
			{
				StartNewObject(container: false);
				_stateStack.Push(new StateElement { type = State.String });
				_writer.Write("\"");
			}

			public void WriteStringEnd()
			{
				Assert(state == State.String);
				_writer.Write("\"");
				_stateStack.Pop();
			}

			public void WriteStringInner(string str, bool escape = true)
			{
				Assert(state == State.String);
				if (escape)
					WriteEscapedString(str);
				else
					_writer.Write(str);
			}

			void WriteEscapedString(string str)
			{
				foreach (var c in str)
				{
					if (c < ' ')
					{
						// Don't write any control characters except \n and \t
						switch (c)
						{
							case '\n':
								_writer.Write("\\n");
								break;
							case '\t':
								_writer.Write("\\t");
								break;
						}
					}
					else
					{
						switch (c)
						{
							case '\\':
							case '"':
								_writer.Write("\\");
								_writer.Write(c);
								break;
							default:
								_writer.Write(c);
								break;
						}
					}
				}
			}

			void StartNewObject(bool container)
			{

				if (container)
					Assert(state == State.None || state == State.Property || state == State.Array);
				else
					Assert(state == State.Property || state == State.Array);

				if (state == State.Array && childCount > 0)
					_writer.Write(",");

				if (state == State.Property)
					Assert(childCount == 0);

				if (state == State.Array || state == State.Property)
					IncrementChildCount();
			}

			State state
			{
				get
				{
					if (_stateStack.Count > 0) return _stateStack.Peek().type;
					else return State.None;
				}
			}

			int childCount
			{
				get
				{
					if (_stateStack.Count > 0) return _stateStack.Peek().childCount;
					else return 0;
				}
			}

			void IncrementChildCount()
			{
				Assert(_stateStack.Count > 0);
				var currEl = _stateStack.Pop();
				currEl.childCount++;
				_stateStack.Push(currEl);
			}

			// Shouldn't hit this assert outside of initial JSON development,
			// so it's save to make it debug-only.
			[System.Diagnostics.Conditional("DEBUG")]
			void Assert(bool condition)
			{
				if (!condition)
					throw new System.Exception("Assert failed while writing JSON");
			}

			public override string ToString()
			{
				return _writer.ToString();
			}

			enum State
			{
				None,
				Object,
				Array,
				Property,
				PropertyName,
				String
			};

			struct StateElement
			{
				public State type;
				public int childCount;
			}

			Stack<StateElement> _stateStack = new Stack<StateElement>();
			TextWriter _writer;
		}
	}
	public enum PushPopType{
		Tunnel,
		Function,
		FunctionEvaluationFromGame
	}
	public delegate void ErrorHandler(string message, ErrorType type);
	public enum ErrorType{
		/// Generated by a "TODO" note in the ink source
		Author,
		/// You should probably fix this, but it's not critical
		Warning,
		/// Critical error that can't be recovered from
		Error
	}
	public static class StringExt{
		public static string Join<T>(string separator, List<T> objects)
		{
			var sb = new StringBuilder ();

			var isFirst = true;
			foreach (var o in objects) {

				if (!isFirst)
					sb.Append (separator);

				sb.Append (o.ToString ());

				isFirst = false;
			}

			return sb.ToString ();
		}
	}
	public class Path : IEquatable<Path>{
		static string parentId = "^";

		// Immutable Component
		public class Component : IEquatable<Component>
		{
			public int index { get; private set; }
			public string name { get; private set; }
			public bool isIndex { get { return index >= 0; } }
			public bool isParent {
				get {
					return name == Path.parentId;
				}
			}

			public Component(int index)
			{
				Debug.Assert(index >= 0);
				this.index = index;
				this.name = null;
			}

			public Component(string name)
			{
				Debug.Assert(name != null && name.Length > 0);
				this.name = name;
				this.index = -1;
			}

			public static Component ToParent()
			{
				return new Component (parentId);
			}

			public override string ToString ()
			{
				if (isIndex) {
					return index.ToString ();
				} else {
					return name;
				}
			}

			public override bool Equals (object obj)
			{
				return Equals (obj as Component);
			}

			public bool Equals(Component otherComp)
			{
				if (otherComp != null && otherComp.isIndex == this.isIndex) {
					if (isIndex) {
						return index == otherComp.index;   
					} else {
						return name == otherComp.name;
					}
				}

				return false;
			}

			public override int GetHashCode ()
			{
				if (isIndex)
					return this.index;
				else
					return this.name.GetHashCode ();
			}
		}

		public Component GetComponent(int index)
		{
			return _components[index];
		}

		public bool isRelative { get; private set; }

		public Component head 
		{ 
			get 
			{ 
				if (_components.Count > 0) {
					return _components.First ();
				} else {
					return null;
				}
			} 
		}

		public Path tail 
		{ 
			get 
			{
				if (_components.Count >= 2) {
					List<Component> tailComps = _components.GetRange (1, _components.Count - 1);
					return new Path(tailComps);
				} 

				else {
					return Path.self;
				}

			}
		}
			
		public int length { get { return _components.Count; } }

		public Component lastComponent 
		{ 
			get 
			{ 
				var lastComponentIdx = _components.Count-1;
				if( lastComponentIdx >= 0 )
					return _components[lastComponentIdx];
				else
					return null;
			} 
		}

		public bool containsNamedComponent {
			get {
				foreach(var comp in _components) {
					if( !comp.isIndex ) {
						return true;
					}
				}
				return false;
			}
		}

		public Path()
		{
			_components = new List<Component> ();
		}

		public Path(Component head, Path tail) : this()
		{
			_components.Add (head);
			_components.AddRange (tail._components);
		}

		public Path(IEnumerable<Component> components, bool relative = false) : this()
		{
			this._components.AddRange (components);
			this.isRelative = relative;
		}

		public Path(string componentsString) : this()
		{
			this.componentsString = componentsString;
		}

		public static Path self {
			get {
				var path = new Path ();
				path.isRelative = true;
				return path;
			}
		}

		public Path PathByAppendingPath(Path pathToAppend)
		{
			Path p = new Path ();

			int upwardMoves = 0;
			for (int i = 0; i < pathToAppend._components.Count; ++i) {
				if (pathToAppend._components [i].isParent) {
					upwardMoves++;
				} else {
					break;
				}
			}

			for (int i = 0; i < this._components.Count - upwardMoves; ++i) {
				p._components.Add (this._components [i]);
			}

			for(int i=upwardMoves; i<pathToAppend._components.Count; ++i) {
				p._components.Add (pathToAppend._components [i]);
			}

			return p;
		}

		public Path PathByAppendingComponent (Component c)
		{
			Path p = new Path ();
			p._components.AddRange (_components);
			p._components.Add (c);
			return p;
		}

		public string componentsString {
			get {
				if( _componentsString == null ) {
					_componentsString = StringExt.Join (".", _components);
					if (isRelative) _componentsString = "." + _componentsString;
				}
				return _componentsString;
			}
			private set {
				_components.Clear ();

				_componentsString = value;

				// Empty path, empty components
				// (path is to root, like "/" in file system)
				if (string.IsNullOrEmpty(_componentsString))
					return;

				// When components start with ".", it indicates a relative path, e.g.
				//   .^.^.hello.5
				// is equivalent to file system style path:
				//  ../../hello/5
				if (_componentsString [0] == '.') {
					this.isRelative = true;
					_componentsString = _componentsString.Substring (1);
				} else {
					this.isRelative = false;
				}

				var componentStrings = _componentsString.Split('.');
				foreach (var str in componentStrings) {
					int index;
					if (int.TryParse (str , out index)) {
						_components.Add (new Component (index));
					} else {
						_components.Add (new Component (str));
					}
				}
			}
		}
		string _componentsString;

		public override string ToString()
		{
			return componentsString;
		}

		public override bool Equals (object obj)
		{
			return Equals (obj as Path);
		}

		public bool Equals (Path otherPath)
		{
			if (otherPath == null)
				return false;

			if (otherPath._components.Count != this._components.Count)
				return false;

			if (otherPath.isRelative != this.isRelative)
				return false;

			return otherPath._components.SequenceEqual (this._components);
		}

		public override int GetHashCode ()
		{
			// TODO: Better way to make a hash code!
			return this.ToString ().GetHashCode ();
		}

		List<Component> _components;
	}
	public class DebugMetadata{
		public int startLineNumber = 0;
		public int endLineNumber = 0;
		public int startCharacterNumber = 0;
		public int endCharacterNumber = 0;
		public string fileName = null;
		public string sourceName = null;

		public DebugMetadata ()
		{
		}

		// Currently only used in VariableReference in order to
		// merge the debug metadata of a Path.Of.Indentifiers into
		// one single range.
		public DebugMetadata Merge(DebugMetadata dm)
		{
			var newDebugMetadata = new DebugMetadata();

			// These are not supposed to be differ between 'this' and 'dm'.
			newDebugMetadata.fileName = fileName;
			newDebugMetadata.sourceName = sourceName;

			if (startLineNumber < dm.startLineNumber)
			{
				newDebugMetadata.startLineNumber = startLineNumber;
				newDebugMetadata.startCharacterNumber = startCharacterNumber;
			}
			else if (startLineNumber > dm.startLineNumber)
			{
				newDebugMetadata.startLineNumber = dm.startLineNumber;
				newDebugMetadata.startCharacterNumber = dm.startCharacterNumber;
			}
			else
			{
				newDebugMetadata.startLineNumber = startLineNumber;
				newDebugMetadata.startCharacterNumber = Math.Min(startCharacterNumber, dm.startCharacterNumber);
			}

			if (endLineNumber > dm.endLineNumber)
			{
				newDebugMetadata.endLineNumber = endLineNumber;
				newDebugMetadata.endCharacterNumber = endCharacterNumber;
			}
			else if (endLineNumber < dm.endLineNumber)
			{
				newDebugMetadata.endLineNumber = dm.endLineNumber;
				newDebugMetadata.endCharacterNumber = dm.endCharacterNumber;
			}
			else
			{
				newDebugMetadata.endLineNumber = endLineNumber;
				newDebugMetadata.endCharacterNumber = Math.Max(endCharacterNumber, dm.endCharacterNumber);
			}

			return newDebugMetadata;
		}

		public override string ToString ()
		{
			if (fileName != null) {
				return string.Format ("line {0} of {1}", startLineNumber, fileName);
			} else {
				return "line " + startLineNumber;
			}

		}
	}
	public struct SearchResult{
		public Runtime.Object obj;
		public bool approximate;

		public Runtime.Object correctObj { get { return approximate ? null : obj; } }
		public Container container { get { return obj as Container; } }
	}
	public struct InkListItem{
		/// <summary>
		/// The name of the list where the item was originally defined.
		/// </summary>
		public readonly string originName;

		/// <summary>
		/// The main name of the item as defined in ink.
		/// </summary>
		public readonly string itemName;

		/// <summary>
		/// Create an item with the given original list definition name, and the name of this
		/// item.
		/// </summary>
		public InkListItem (string originName, string itemName)
		{
			this.originName = originName;
			this.itemName = itemName;
		}

		/// <summary>
		/// Create an item from a dot-separted string of the form "listDefinitionName.listItemName".
		/// </summary>
		public InkListItem (string fullName)
		{
			var nameParts = fullName.Split ('.');
			this.originName = nameParts [0];
			this.itemName = nameParts [1];
		}

		public static InkListItem Null {
			get {
				return new InkListItem (null, null);
			}
		}

		public bool isNull {
			get {
				return originName == null && itemName == null;
			}
		}

		/// <summary>
		/// Get the full dot-separated name of the item, in the form "listDefinitionName.itemName".
		/// </summary>
		public string fullName {
			get {
				return (originName ?? "?") + "." + itemName;
			}
		}

		/// <summary>
		/// Get the full dot-separated name of the item, in the form "listDefinitionName.itemName".
		/// Calls fullName internally.
		/// </summary>
		public override string ToString ()
		{
			return fullName;
		}

		/// <summary>
		/// Is this item the same as another item?
		/// </summary>
		public override bool Equals (object obj)
		{
			if (obj is InkListItem) {
				var otherItem = (InkListItem)obj;
				return otherItem.itemName   == itemName 
					&& otherItem.originName == originName;
			}

			return false;
		}

		/// <summary>
		/// Get the hashcode for an item.
		/// </summary>
		public override int GetHashCode ()
		{
			int originCode = 0;
			int itemCode = itemName.GetHashCode ();
			if (originName != null)
				originCode = originName.GetHashCode ();
			
			return originCode + itemCode;
		}
	}
	public class ListDefinition{
		public string name { get { return _name; } }

		public Dictionary<InkListItem, int> items {
			get {
				if (_items == null) {
					_items = new Dictionary<InkListItem, int> ();
					foreach (var itemNameAndValue in _itemNameToValues) {
						var item = new InkListItem (name, itemNameAndValue.Key);
						_items [item] = itemNameAndValue.Value;
					}
				}
				return _items;
			}
		}
		Dictionary<InkListItem, int> _items;

		public int ValueForItem (InkListItem item)
		{
			int intVal;
			if (_itemNameToValues.TryGetValue (item.itemName, out intVal))
				return intVal;
			else
				return 0;
		}

		public bool ContainsItem (InkListItem item)
		{
			if (item.originName != name) return false;

			return _itemNameToValues.ContainsKey (item.itemName);
		}

		public bool ContainsItemWithName (string itemName)
		{
			return _itemNameToValues.ContainsKey (itemName);
		}

		public bool TryGetItemWithValue (int val, out InkListItem item)
		{
			foreach (var namedItem in _itemNameToValues) {
				if (namedItem.Value == val) {
					item = new InkListItem (name, namedItem.Key);
					return true;
				}
			}

			item = InkListItem.Null;
			return false;
		}

		public bool TryGetValueForItem (InkListItem item, out int intVal)
		{
			return _itemNameToValues.TryGetValue (item.itemName, out intVal);
		}

		public ListDefinition (string name, Dictionary<string, int> items)
		{
			_name = name;
			_itemNameToValues = items;
		}

		string _name;

		// The main representation should be simple item names rather than a RawListItem,
		// since we mainly want to access items based on their simple name, since that's
		// how they'll be most commonly requested from ink.
		Dictionary<string, int> _itemNameToValues;
	}
	public class StoryException : System.Exception {
		public bool useEndLineNumber;

		/// <summary>
		/// Constructs a default instance of a StoryException without a message.
		/// </summary>
		public StoryException () { }

		/// <summary>
		/// Constructs an instance of a StoryException with a message.
		/// </summary>
		/// <param name="message">The error message.</param>
		public StoryException(string message) : base(message) {}
	}
	public class Object{
		/// <summary>
		/// Runtime.Objects can be included in the main Story as a hierarchy.
		/// Usually parents are Container objects. (TODO: Always?)
		/// </summary>
		/// <value>The parent.</value>
		public Runtime.Object parent { get; set; }

		public Runtime.DebugMetadata debugMetadata { 
			get {
				if (_debugMetadata == null) {
					if (parent) {
						return parent.debugMetadata;
					}
				}

				return _debugMetadata;
			}

			set {
				_debugMetadata = value;
			}
		}

		public Runtime.DebugMetadata ownDebugMetadata {
			get {
				return _debugMetadata;
			}
		}

		// TODO: Come up with some clever solution for not having
		// to have debug metadata on the object itself, perhaps
		// for serialisation purposes at least.
		DebugMetadata _debugMetadata;

		public int? DebugLineNumberOfPath(Path path)
		{
			if (path == null)
				return null;
			
			// Try to get a line number from debug metadata
			var root = this.rootContentContainer;
			if (root) {
				Runtime.Object targetContent = root.ContentAtPath (path).obj;
				if (targetContent) {
					var dm = targetContent.debugMetadata;
					if (dm != null) {
						return dm.startLineNumber;
					}
				}
			}

			return null;
		}

		public Path path 
		{ 
			get 
			{
				if (_path == null) {

					if (parent == null) {
						_path = new Path ();
					} else {
						// Maintain a Stack so that the order of the components
						// is reversed when they're added to the Path.
						// We're iterating up the hierarchy from the leaves/children to the root.
						var comps = new Stack<Path.Component> ();

						var child = this;
						Container container = child.parent as Container;

						while (container) {

							var namedChild = child as INamedContent;
							if (namedChild != null && namedChild.hasValidName) {
								comps.Push (new Path.Component (namedChild.name));
							} else {
								comps.Push (new Path.Component (container.content.IndexOf(child)));
							}

							child = container;
							container = container.parent as Container;
						}

						_path = new Path (comps);
					}

				}
				
				return _path;
			}
		}
		Path _path;

		public SearchResult ResolvePath(Path path)
		{
			if (path.isRelative) {

				Container nearestContainer = this as Container;
				if (!nearestContainer) {
					Debug.Assert (this.parent != null, "Can't resolve relative path because we don't have a parent");
					nearestContainer = this.parent as Container;
					Debug.Assert (nearestContainer != null, "Expected parent to be a container");
					Debug.Assert (path.GetComponent(0).isParent);
					path = path.tail;
				}

				return nearestContainer.ContentAtPath (path);
			} else {
				return this.rootContentContainer.ContentAtPath (path);
			}
		}

		public Path ConvertPathToRelative(Path globalPath)
		{
			// 1. Find last shared ancestor
			// 2. Drill up using ".." style (actually represented as "^")
			// 3. Re-build downward chain from common ancestor

			var ownPath = this.path;

			int minPathLength = Math.Min (globalPath.length, ownPath.length);
			int lastSharedPathCompIndex = -1;

			for (int i = 0; i < minPathLength; ++i) {
				var ownComp = ownPath.GetComponent(i);
				var otherComp = globalPath.GetComponent(i);

				if (ownComp.Equals (otherComp)) {
					lastSharedPathCompIndex = i;
				} else {
					break;
				}
			}

			// No shared path components, so just use global path
			if (lastSharedPathCompIndex == -1)
				return globalPath;

			int numUpwardsMoves = (ownPath.length-1) - lastSharedPathCompIndex;

			var newPathComps = new List<Path.Component> ();

			for(int up=0; up<numUpwardsMoves; ++up)
				newPathComps.Add (Path.Component.ToParent ());

			for (int down = lastSharedPathCompIndex + 1; down < globalPath.length; ++down)
				newPathComps.Add (globalPath.GetComponent(down));

			var relativePath = new Path (newPathComps, relative:true);
			return relativePath;
		}

		// Find most compact representation for a path, whether relative or global
		public string CompactPathString(Path otherPath)
		{
			string globalPathStr = null;
			string relativePathStr = null;
			if (otherPath.isRelative) {
				relativePathStr = otherPath.componentsString;
				globalPathStr = this.path.PathByAppendingPath(otherPath).componentsString;
			} else {
				var relativePath = ConvertPathToRelative (otherPath);
				relativePathStr = relativePath.componentsString;
				globalPathStr = otherPath.componentsString;
			}

			if (relativePathStr.Length < globalPathStr.Length) 
				return relativePathStr;
			else
				return globalPathStr;
		}

		public Container rootContentContainer
		{
			get 
			{
				Runtime.Object ancestor = this;
				while (ancestor.parent) {
					ancestor = ancestor.parent;
				}
				return ancestor as Container;
			}
		}

		public Object ()
		{
		}

		public virtual Object Copy()
		{
			throw new System.NotImplementedException (GetType ().Name + " doesn't support copying");
		}

		public void SetChild<T>(ref T obj, T value) where T : Runtime.Object
		{
			if (obj)
				obj.parent = null;

			obj = value;

			if( obj )
				obj.parent = this;
		}
			
		/// Allow implicit conversion to bool so you don't have to do:
		/// if( myObj != null ) ...
		public static implicit operator bool (Object obj)
		{
			var isNull = object.ReferenceEquals (obj, null);
			return !isNull;
		}

		/// Required for implicit bool comparison
		public static bool operator ==(Object a, Object b)
		{
			return object.ReferenceEquals (a, b);
		}

		/// Required for implicit bool comparison
		public static bool operator !=(Object a, Object b)
		{
			return !(a == b);
		}

		/// Required for implicit bool comparison
		public override bool Equals (object obj)
		{
			return object.ReferenceEquals (obj, this);
		}

		/// Required for implicit bool comparison
		public override int GetHashCode ()
		{
			return base.GetHashCode ();
		}
	}
	public abstract class Value : Runtime.Object{
		public abstract ValueType valueType { get; }
		public abstract bool isTruthy { get; }

		public abstract Value Cast(ValueType newType);

		public abstract object valueObject { get; }

		public static Value Create(object val){
			// Implicitly lose precision from any doubles we get passed in
			if (val is double) {
				double doub = (double)val;
				val = (float)doub;
			}

			if( val is bool ) {
				return new BoolValue((bool)val);
			} else if (val is int) {
				return new IntValue ((int)val);
			} else if (val is long) {
				return new IntValue ((int)(long)val);
			} else if (val is float) {
				return new FloatValue ((float)val);
			} else if (val is double) {
				return new FloatValue ((float)(double)val);
			} else if (val is string) {
				return new StringValue ((string)val);
			} else if (val is Path) {
				return new DivertTargetValue ((Path)val);
			} else if (val is InkList) {
				return new ListValue ((InkList)val);
			}

			return null;
		}

		public override Object Copy()
		{
			return Create (valueObject);
		}

		protected StoryException BadCastException (ValueType targetType)
		{
			return new StoryException ("Can't cast "+this.valueObject+" from " + this.valueType+" to "+targetType);
		}
	}
	public enum ValueType{
		// Bool is new addition, keep enum values the same, with Int==0, Float==1 etc,
		// but for coersion rules, we want to keep bool with a lower value than Int
		// so that it converts in the right direction
		Bool = -1, 
		// Used in coersion
		Int,
		Float,
		List,
		String,

		// Not used for coersion described above
		DivertTarget,
		VariablePointer
	}
	public class BoolValue : Value<bool>{
		public override ValueType valueType { get { return ValueType.Bool; } }
		public override bool isTruthy { get { return value; } }

		public BoolValue(bool boolVal) : base(boolVal)
		{
		}

		public BoolValue() : this(false) {}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType) {
				return this;
			}

			if (newType == ValueType.Int) {
				return new IntValue (this.value ? 1 : 0);
			}

			if (newType == ValueType.Float) {
				return new FloatValue (this.value ? 1.0f : 0.0f);
			}

			if (newType == ValueType.String) {
				return new StringValue(this.value ? "true" : "false");
			}

			throw BadCastException (newType);
		}

		public override string ToString ()
		{
			// Instead of C# "True" / "False"
			return value ? "true" : "false";
		}
	}
	public class IntValue : Value<int>{
		public override ValueType valueType { get { return ValueType.Int; } }
		public override bool isTruthy { get { return value != 0; } }

		public IntValue(int intVal) : base(intVal)
		{
		}

		public IntValue() : this(0) {}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType) {
				return this;
			}

			if (newType == ValueType.Bool) {
				return new BoolValue (this.value == 0 ? false : true);
			}

			if (newType == ValueType.Float) {
				return new FloatValue ((float)this.value);
			}

			if (newType == ValueType.String) {
				return new StringValue("" + this.value);
			}

			throw BadCastException (newType);
		}
	}
	public class FloatValue : Value<float>{
		public override ValueType valueType { get { return ValueType.Float; } }
		public override bool isTruthy { get { return value != 0.0f; } }

		public FloatValue(float val) : base(val)
		{
		}

		public FloatValue() : this(0.0f) {}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType) {
				return this;
			}

			if (newType == ValueType.Bool) {
				return new BoolValue (this.value == 0.0f ? false : true);
			}

			if (newType == ValueType.Int) {
				return new IntValue ((int)this.value);
			}

			if (newType == ValueType.String) {
				return new StringValue("" + this.value.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}

			throw BadCastException (newType);
		}
	}
	public class StringValue : Value<string>{
		public override ValueType valueType { get { return ValueType.String; } }
		public override bool isTruthy { get { return value.Length > 0; } }

		public bool isNewline { get; private set; }
		public bool isInlineWhitespace { get; private set; }
		public bool isNonWhitespace {
			get {
				return !isNewline && !isInlineWhitespace;
			}
		}

		public StringValue(string str) : base(str)
		{
			// Classify whitespace status
			isNewline = value == "\n";
			isInlineWhitespace = true;
			foreach (var c in value) {
				if (c != ' ' && c != '\t') {
					isInlineWhitespace = false;
					break;
				}
			}
		}

		public StringValue() : this("") {}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType) {
				return this;
			}

			if (newType == ValueType.Int) {

				int parsedInt;
				if (int.TryParse (value, out parsedInt)) {
					return new IntValue (parsedInt);
				} else {
					return null;
				}
			}

			if (newType == ValueType.Float) {
				float parsedFloat;
				if (float.TryParse (value, System.Globalization.NumberStyles.Float ,System.Globalization.CultureInfo.InvariantCulture, out parsedFloat)) {
					return new FloatValue (parsedFloat);
				} else {
					return null;
				}
			}

			throw BadCastException (newType);
		}
	}
	public abstract class Value<T> : Value{
		public T value { get; set; }

		public override object valueObject {
			get {
				return (object)value;
			}
		}

		public Value (T val)
		{
			value = val;
		}

		public override string ToString ()
		{
			return value.ToString();
		}
	}
	public class Glue : Runtime.Object{
		public Glue() { }

		public override string ToString ()
		{
			return "Glue";
		}
	}
	public class Divert : Runtime.Object{
		public Path targetPath { 
			get { 
				// Resolve any relative paths to global ones as we come across them
				if (_targetPath != null && _targetPath.isRelative) {
					var targetObj = targetPointer.Resolve();
					if (targetObj) {
						_targetPath = targetObj.path;
					}
				}
				return _targetPath;
			}
			set {
				_targetPath = value;
				_targetPointer = Pointer.Null;
			} 
		}
		Path _targetPath;

		public Pointer targetPointer {
			get {
				if (_targetPointer.isNull) {
					var targetObj = ResolvePath (_targetPath).obj;

					if (_targetPath.lastComponent.isIndex) {
						_targetPointer.container = targetObj.parent as Container;
						_targetPointer.index = _targetPath.lastComponent.index;
					} else {
						_targetPointer = Pointer.StartOf (targetObj as Container);
					}
				}
				return _targetPointer;
			}
		}
		Pointer _targetPointer;
		

		public string targetPathString {
			get {
				if (targetPath == null)
					return null;

				return CompactPathString (targetPath);
			}
			set {
				if (value == null) {
					targetPath = null;
				} else {
					targetPath = new Path (value);
				}
			}
		}
			
		public string variableDivertName { get; set; }
		public bool hasVariableTarget { get { return variableDivertName != null; } }

		public bool pushesToStack { get; set; }
		public PushPopType stackPushType;

		public bool isExternal { get; set; }
		public int externalArgs { get; set; }

		public bool isConditional { get; set; }

		public Divert ()
		{
			pushesToStack = false;
		}

		public Divert(PushPopType stackPushType)
		{
			pushesToStack = true;
			this.stackPushType = stackPushType;
		}

		public override bool Equals (object obj)
		{
			var otherDivert = obj as Divert;
			if (otherDivert) {
				if (this.hasVariableTarget == otherDivert.hasVariableTarget) {
					if (this.hasVariableTarget) {
						return this.variableDivertName == otherDivert.variableDivertName;
					} else {
						return this.targetPath.Equals(otherDivert.targetPath);
					}
				}
			}
			return false;
		}

		public override int GetHashCode ()
		{
			if (hasVariableTarget) {
				const int variableTargetSalt = 12345;
				return variableDivertName.GetHashCode() + variableTargetSalt;
			} else {
				const int pathTargetSalt = 54321;
				return targetPath.GetHashCode() + pathTargetSalt;
			}
		}

		public override string ToString ()
		{
			if (hasVariableTarget) {
				return "Divert(variable: " + variableDivertName + ")";
			}
			else if (targetPath == null) {
				return "Divert(null)";
			} else {

				var sb = new StringBuilder ();

				string targetStr = targetPath.ToString ();
				int? targetLineNum = DebugLineNumberOfPath (targetPath);
				if (targetLineNum != null) {
					targetStr = "line " + targetLineNum;
				}

				sb.Append ("Divert");

				if (isConditional)
					sb.Append ("?");

				if (pushesToStack) {
					if (stackPushType == PushPopType.Function) {
						sb.Append (" function");
					} else {
						sb.Append (" tunnel");
					}
				}

				sb.Append (" -> ");
				sb.Append (targetPathString);

				sb.Append (" (");
				sb.Append (targetStr);
				sb.Append (")");

				return sb.ToString ();
			}
		}
	}
	public class ControlCommand : Runtime.Object{
		public enum CommandType
		{
			NotSet = -1,
			EvalStart,
			EvalOutput,
			EvalEnd,
			Duplicate,
			PopEvaluatedValue,
			PopFunction,
			PopTunnel,
			BeginString,
			EndString,
			NoOp,
			ChoiceCount,
			Turns,
			TurnsSince,
			ReadCount,
			Random,
			SeedRandom,
			VisitIndex,
			SequenceShuffleIndex,
			StartThread,
			Done,
			End,
			ListFromInt,
			ListRange,
			ListRandom,
			//----
			TOTAL_VALUES
		}
			
		public CommandType commandType { get; protected set; }

		public ControlCommand (CommandType commandType)
		{
			this.commandType = commandType;
		}

		// Require default constructor for serialisation
		public ControlCommand() : this(CommandType.NotSet) {}

		public override Object Copy()
		{
			return new ControlCommand (commandType);
		}

		// The following static factory methods are to make generating these objects
		// slightly more succinct. Without these, the code gets pretty massive! e.g.
		//
		//     var c = new Runtime.ControlCommand(Runtime.ControlCommand.CommandType.EvalStart)
		// 
		// as opposed to
		//
		//     var c = Runtime.ControlCommand.EvalStart()

		public static ControlCommand EvalStart() {
			return new ControlCommand(CommandType.EvalStart);
		}

		public static ControlCommand EvalOutput() {
			return new ControlCommand(CommandType.EvalOutput);
		}

		public static ControlCommand EvalEnd() {
			return new ControlCommand(CommandType.EvalEnd);
		}

		public static ControlCommand Duplicate() {
			return new ControlCommand(CommandType.Duplicate);
		}

		public static ControlCommand PopEvaluatedValue() {
			return new ControlCommand (CommandType.PopEvaluatedValue);
		}

		public static ControlCommand PopFunction() {
			return new ControlCommand (CommandType.PopFunction);
		}

		public static ControlCommand PopTunnel() {
			return new ControlCommand (CommandType.PopTunnel);
		}
			
		public static ControlCommand BeginString() {
			return new ControlCommand (CommandType.BeginString);
		}

		public static ControlCommand EndString() {
			return new ControlCommand (CommandType.EndString);
		}

		public static ControlCommand NoOp() {
			return new ControlCommand(CommandType.NoOp);
		}

		public static ControlCommand ChoiceCount() {
			return new ControlCommand(CommandType.ChoiceCount);
		}

		public static ControlCommand Turns ()
		{
			return new ControlCommand (CommandType.Turns);
		}

		public static ControlCommand TurnsSince() {
			return new ControlCommand(CommandType.TurnsSince);
		}

		public static ControlCommand ReadCount ()
		{
			return new ControlCommand (CommandType.ReadCount);
		}

		public static ControlCommand Random ()
		{
			return new ControlCommand (CommandType.Random);
		}

		public static ControlCommand SeedRandom ()
		{
			return new ControlCommand (CommandType.SeedRandom);
		}

		public static ControlCommand VisitIndex() {
			return new ControlCommand(CommandType.VisitIndex);
		}
			
		public static ControlCommand SequenceShuffleIndex() {
			return new ControlCommand(CommandType.SequenceShuffleIndex);
		}

		public static ControlCommand StartThread() {
			return new ControlCommand (CommandType.StartThread);
		}

		public static ControlCommand Done() {
			return new ControlCommand (CommandType.Done);
		}

		public static ControlCommand End() {
			return new ControlCommand (CommandType.End);
		}

		public static ControlCommand ListFromInt () {
			return new ControlCommand (CommandType.ListFromInt);
		}

		public static ControlCommand ListRange ()
		{
			return new ControlCommand (CommandType.ListRange);
		}

		public static ControlCommand ListRandom ()
		{
			return new ControlCommand (CommandType.ListRandom);
		}

		public override string ToString ()
		{
			return commandType.ToString();
		}
	}
	public class Void : Runtime.Object{
		public Void ()
		{
		}
	}
	public class NativeFunctionCall : Runtime.Object{
		public const string Add      = "+";
		public const string Subtract = "-";
		public const string Divide   = "/";
		public const string Multiply = "*";
		public const string Mod      = "%";
		public const string Negate   = "_"; // distinguish from "-" for subtraction

		public const string Equal    = "==";
		public const string Greater  = ">";
		public const string Less     = "<";
		public const string GreaterThanOrEquals = ">=";
		public const string LessThanOrEquals = "<=";
		public const string NotEquals   = "!=";
		public const string Not      = "!";



		public const string And      = "&&";
		public const string Or       = "||";

		public const string Min      = "MIN";
		public const string Max      = "MAX";

		public const string Pow      = "POW";
		public const string Floor    = "FLOOR";
		public const string Ceiling  = "CEILING";
		public const string Int      = "INT";
		public const string Float    = "FLOAT";

		public const string Has      = "?";
		public const string Hasnt    = "!?";
		public const string Intersect = "^";

		public const string ListMin   = "LIST_MIN";
		public const string ListMax   = "LIST_MAX";
		public const string All       = "LIST_ALL";
		public const string Count     = "LIST_COUNT";
		public const string ValueOfList = "LIST_VALUE";
		public const string Invert    = "LIST_INVERT";

		public static NativeFunctionCall CallWithName(string functionName)
		{
			return new NativeFunctionCall (functionName);
		}

		public static bool CallExistsWithName(string functionName)
		{
			GenerateNativeFunctionsIfNecessary ();
			return _nativeFunctions.ContainsKey (functionName);
		}
			
		public string name { 
			get { 
				return _name;
			} 
			protected set {
				_name = value;
				if( !_isPrototype )
					_prototype = _nativeFunctions [_name];
			}
		}
		string _name;

		public int numberOfParameters { 
			get {
				if (_prototype) {
					return _prototype.numberOfParameters;
				} else {
					return _numberOfParameters;
				}
			}
			protected set {
				_numberOfParameters = value;
			}
		}

		int _numberOfParameters;

		public Runtime.Object Call(List<Runtime.Object> parameters)
		{
			if (_prototype) {
				return _prototype.Call(parameters);
			}

			if (numberOfParameters != parameters.Count) {
				throw new System.Exception ("Unexpected number of parameters");
			}

			bool hasList = false;
			foreach (var p in parameters) {
				if (p is Void)
					throw new StoryException ("Attempting to perform operation on a void value. Did you forget to 'return' a value from a function you called here?");
				if (p is ListValue)
					hasList = true;
			}

			// Binary operations on lists are treated outside of the standard coerscion rules
			if( parameters.Count == 2 && hasList )
				return CallBinaryListOperation (parameters);

			var coercedParams = CoerceValuesToSingleType (parameters);
			ValueType coercedType = coercedParams[0].valueType;

			if (coercedType == ValueType.Int) {
				return Call<int> (coercedParams);
			} else if (coercedType == ValueType.Float) {
				return Call<float> (coercedParams);
			} else if (coercedType == ValueType.String) {
				return Call<string> (coercedParams);
			} else if (coercedType == ValueType.DivertTarget) {
				return Call<Path> (coercedParams);
			} else if (coercedType == ValueType.List) {
				return Call<InkList> (coercedParams);
			}

			return null;
		}

		Value Call<T>(List<Value> parametersOfSingleType)
		{
			Value param1 = (Value) parametersOfSingleType [0];
			ValueType valType = param1.valueType;

			var val1 = (Value<T>)param1;

			int paramCount = parametersOfSingleType.Count;

			if (paramCount == 2 || paramCount == 1) {

				object opForTypeObj = null;
				if (!_operationFuncs.TryGetValue (valType, out opForTypeObj)) {
					throw new StoryException ("Cannot perform operation '"+this.name+"' on "+valType);
				}

				// Binary
				if (paramCount == 2) {
					Value param2 = (Value) parametersOfSingleType [1];

					var val2 = (Value<T>)param2;

					var opForType = (BinaryOp<T>)opForTypeObj;

					// Return value unknown until it's evaluated
					object resultVal = opForType (val1.value, val2.value);

					return Value.Create (resultVal);
				} 

				// Unary
				else {

					var opForType = (UnaryOp<T>)opForTypeObj;

					var resultVal = opForType (val1.value);

					return Value.Create (resultVal);
				}  
			}
				
			else {
				throw new System.Exception ("Unexpected number of parameters to NativeFunctionCall: " + parametersOfSingleType.Count);
			}
		}

		Value CallBinaryListOperation (List<Runtime.Object> parameters)
		{
			// List-Int addition/subtraction returns a List (e.g. "alpha" + 1 = "beta")
			if ((name == "+" || name == "-") && parameters [0] is ListValue && parameters [1] is IntValue)
				return CallListIncrementOperation (parameters);

			var v1 = parameters [0] as Value;
			var v2 = parameters [1] as Value;

			// And/or with any other type requires coerscion to bool (int)
			if ((name == "&&" || name == "||") && (v1.valueType != ValueType.List || v2.valueType != ValueType.List)) {
				var op = _operationFuncs [ValueType.Int] as BinaryOp<int>;
				var result = (bool)op (v1.isTruthy ? 1 : 0, v2.isTruthy ? 1 : 0);
				return new BoolValue (result);
			}

			// Normal (list • list) operation
			if (v1.valueType == ValueType.List && v2.valueType == ValueType.List)
				return Call<InkList> (new List<Value> { v1, v2 });

			throw new StoryException ("Can not call use '" + name + "' operation on " + v1.valueType + " and " + v2.valueType);
		}

		Value CallListIncrementOperation (List<Runtime.Object> listIntParams)
		{
			var listVal = (ListValue)listIntParams [0];
			var intVal = (IntValue)listIntParams [1];


			var resultRawList = new InkList ();

			foreach (var listItemWithValue in listVal.value) {
				var listItem = listItemWithValue.Key;
				var listItemValue = listItemWithValue.Value;

				// Find + or - operation
				var intOp = (BinaryOp<int>)_operationFuncs [ValueType.Int];

				// Return value unknown until it's evaluated
				int targetInt = (int) intOp (listItemValue, intVal.value);

				// Find this item's origin (linear search should be ok, should be short haha)
				ListDefinition itemOrigin = null;
				foreach (var origin in listVal.value.origins) {
					if (origin.name == listItem.originName) {
						itemOrigin = origin;
						break;
					}
				}
				if (itemOrigin != null) {
					InkListItem incrementedItem;
					if (itemOrigin.TryGetItemWithValue (targetInt, out incrementedItem))
						resultRawList.Add (incrementedItem, targetInt);
				}
			}

			return new ListValue (resultRawList);
		}

		List<Value> CoerceValuesToSingleType(List<Runtime.Object> parametersIn)
		{
			ValueType valType = ValueType.Int;

			ListValue specialCaseList = null;

			// Find out what the output type is
			// "higher level" types infect both so that binary operations
			// use the same type on both sides. e.g. binary operation of
			// int and float causes the int to be casted to a float.
			foreach (var obj in parametersIn) {
				var val = (Value)obj;
				if (val.valueType > valType) {
					valType = val.valueType;
				}

				if (val.valueType == ValueType.List) {
					specialCaseList = val as ListValue;
				}
			}

			// Coerce to this chosen type
			var parametersOut = new List<Value> ();

			// Special case: Coercing to Ints to Lists
			// We have to do it early when we have both parameters
			// to hand - so that we can make use of the List's origin
			if (valType == ValueType.List) {
				
				foreach (Value val in parametersIn) {
					if (val.valueType == ValueType.List) {
						parametersOut.Add (val);
					} else if (val.valueType == ValueType.Int) {
						int intVal = (int)val.valueObject;
						var list = specialCaseList.value.originOfMaxItem;

						InkListItem item;
						if (list.TryGetItemWithValue (intVal, out item)) {
							var castedValue = new ListValue (item, intVal);
							parametersOut.Add (castedValue);
						} else
							throw new StoryException ("Could not find List item with the value " + intVal + " in " + list.name);
					} else
						throw new StoryException ("Cannot mix Lists and " + val.valueType + " values in this operation");
				}
				
			} 

			// Normal Coercing (with standard casting)
			else {
				foreach (Value val in parametersIn) {
					var castedValue = val.Cast (valType);
					parametersOut.Add (castedValue);
				}
			}

			return parametersOut;
		}

		public NativeFunctionCall(string name)
		{
			GenerateNativeFunctionsIfNecessary ();

			this.name = name;
		}

		// Require default constructor for serialisation
		public NativeFunctionCall() { 
			GenerateNativeFunctionsIfNecessary ();
		}

		// Only called internally to generate prototypes
		NativeFunctionCall (string name, int numberOfParameters)
		{
			_isPrototype = true;
			this.name = name;
			this.numberOfParameters = numberOfParameters;
		}

		// For defining operations that do nothing to the specific type
		// (but are still supported), such as floor/ceil on int and float
		// cast on float.
		static object Identity<T>(T t) {
			return t;
		}

		static void GenerateNativeFunctionsIfNecessary()
		{
			if (_nativeFunctions == null) {
				_nativeFunctions = new Dictionary<string, NativeFunctionCall> ();

				// Why no bool operations?
				// Before evaluation, all bools are coerced to ints in
				// CoerceValuesToSingleType (see default value for valType at top).
				// So, no operations are ever directly done in bools themselves.
				// This also means that 1 == true works, since true is always converted
				// to 1 first.
				// However, many operations return a "native" bool (equals, etc).

				// Int operations
				AddIntBinaryOp(Add,      (x, y) => x + y);
				AddIntBinaryOp(Subtract, (x, y) => x - y);
				AddIntBinaryOp(Multiply, (x, y) => x * y);
				AddIntBinaryOp(Divide,   (x, y) => x / y);
				AddIntBinaryOp(Mod,      (x, y) => x % y); 
				AddIntUnaryOp (Negate,   x => -x); 

				AddIntBinaryOp(Equal,    (x, y) => x == y);
				AddIntBinaryOp(Greater,  (x, y) => x > y);
				AddIntBinaryOp(Less,     (x, y) => x < y);
				AddIntBinaryOp(GreaterThanOrEquals, (x, y) => x >= y);
				AddIntBinaryOp(LessThanOrEquals, (x, y) => x <= y);
				AddIntBinaryOp(NotEquals, (x, y) => x != y);
				AddIntUnaryOp (Not,       x => x == 0); 

				AddIntBinaryOp(And,      (x, y) => x != 0 && y != 0);
				AddIntBinaryOp(Or,       (x, y) => x != 0 || y != 0);

				AddIntBinaryOp(Max,      (x, y) => Math.Max(x, y));
				AddIntBinaryOp(Min,      (x, y) => Math.Min(x, y));

				// Have to cast to float since you could do POW(2, -1)
				AddIntBinaryOp (Pow,      (x, y) => (float) Math.Pow(x, y));
				AddIntUnaryOp(Floor,      Identity);
				AddIntUnaryOp(Ceiling,    Identity);
				AddIntUnaryOp(Int,        Identity);
				AddIntUnaryOp (Float,     x => (float)x);

				// Float operations
				AddFloatBinaryOp(Add,      (x, y) => x + y);
				AddFloatBinaryOp(Subtract, (x, y) => x - y);
				AddFloatBinaryOp(Multiply, (x, y) => x * y);
				AddFloatBinaryOp(Divide,   (x, y) => x / y);
				AddFloatBinaryOp(Mod,      (x, y) => x % y); // TODO: Is this the operation we want for floats?
				AddFloatUnaryOp (Negate,   x => -x); 

				AddFloatBinaryOp(Equal,    (x, y) => x == y);
				AddFloatBinaryOp(Greater,  (x, y) => x > y);
				AddFloatBinaryOp(Less,     (x, y) => x < y);
				AddFloatBinaryOp(GreaterThanOrEquals, (x, y) => x >= y);
				AddFloatBinaryOp(LessThanOrEquals, (x, y) => x <= y);
				AddFloatBinaryOp(NotEquals, (x, y) => x != y);
				AddFloatUnaryOp (Not,       x => (x == 0.0f)); 

				AddFloatBinaryOp(And,      (x, y) => x != 0.0f && y != 0.0f);
				AddFloatBinaryOp(Or,       (x, y) => x != 0.0f || y != 0.0f);

				AddFloatBinaryOp(Max,      (x, y) => Math.Max(x, y));
				AddFloatBinaryOp(Min,      (x, y) => Math.Min(x, y));

				AddFloatBinaryOp (Pow,      (x, y) => (float)Math.Pow(x, y));
				AddFloatUnaryOp(Floor,      x => (float)Math.Floor(x));
				AddFloatUnaryOp(Ceiling,    x => (float)Math.Ceiling(x));
				AddFloatUnaryOp(Int,        x => (int)x);
				AddFloatUnaryOp(Float,      Identity);

				// String operations
				AddStringBinaryOp(Add,     (x, y) => x + y); // concat
				AddStringBinaryOp(Equal,   (x, y) => x.Equals(y));
				AddStringBinaryOp (NotEquals, (x, y) => !x.Equals (y));
				AddStringBinaryOp (Has,    (x, y) => x.Contains(y));
				AddStringBinaryOp (Hasnt,   (x, y) => !x.Contains(y));

				// List operations
				AddListBinaryOp (Add, (x, y) => x.Union (y));
				AddListBinaryOp (Subtract, (x, y) => x.Without(y));
				AddListBinaryOp (Has, (x, y) => x.Contains (y));
				AddListBinaryOp (Hasnt, (x, y) => !x.Contains (y));
				AddListBinaryOp (Intersect, (x, y) => x.Intersect (y));

				AddListBinaryOp (Equal, (x, y) => x.Equals(y));
				AddListBinaryOp (Greater, (x, y) => x.GreaterThan(y));
				AddListBinaryOp (Less, (x, y) => x.LessThan(y));
				AddListBinaryOp (GreaterThanOrEquals, (x, y) => x.GreaterThanOrEquals(y));
				AddListBinaryOp (LessThanOrEquals, (x, y) => x.LessThanOrEquals(y));
				AddListBinaryOp (NotEquals, (x, y) => !x.Equals(y));

				AddListBinaryOp (And, (x, y) => x.Count > 0 && y.Count > 0);
				AddListBinaryOp (Or,  (x, y) => x.Count > 0 || y.Count > 0);

				AddListUnaryOp (Not, x => x.Count == 0 ? (int)1 : (int)0);

				// Placeholders to ensure that these special case functions can exist,
				// since these function is never actually run, and is special cased in Call
				AddListUnaryOp (Invert, x => x.inverse);
				AddListUnaryOp (All, x => x.all);
				AddListUnaryOp (ListMin, (x) => x.MinAsList());
				AddListUnaryOp (ListMax, (x) => x.MaxAsList());
				AddListUnaryOp (Count,  (x) => x.Count);
				AddListUnaryOp (ValueOfList,  (x) => x.maxItem.Value);

				// Special case: The only operations you can do on divert target values
				BinaryOp<Path> divertTargetsEqual = (Path d1, Path d2) => {
					return d1.Equals (d2);
				};
				BinaryOp<Path> divertTargetsNotEqual = (Path d1, Path d2) => {
					return !d1.Equals (d2);
				};
				AddOpToNativeFunc (Equal, 2, ValueType.DivertTarget, divertTargetsEqual);
				AddOpToNativeFunc (NotEquals, 2, ValueType.DivertTarget, divertTargetsNotEqual);

			}
		}

		void AddOpFuncForType(ValueType valType, object op)
		{
			if (_operationFuncs == null) {
				_operationFuncs = new Dictionary<ValueType, object> ();
			}

			_operationFuncs [valType] = op;
		}

		static void AddOpToNativeFunc(string name, int args, ValueType valType, object op)
		{
			NativeFunctionCall nativeFunc = null;
			if (!_nativeFunctions.TryGetValue (name, out nativeFunc)) {
				nativeFunc = new NativeFunctionCall (name, args);
				_nativeFunctions [name] = nativeFunc;
			}

			nativeFunc.AddOpFuncForType (valType, op);
		}

		static void AddIntBinaryOp(string name, BinaryOp<int> op)
		{
			AddOpToNativeFunc (name, 2, ValueType.Int, op);
		}

		static void AddIntUnaryOp(string name, UnaryOp<int> op)
		{
			AddOpToNativeFunc (name, 1, ValueType.Int, op);
		}

		static void AddFloatBinaryOp(string name, BinaryOp<float> op)
		{
			AddOpToNativeFunc (name, 2, ValueType.Float, op);
		}

		static void AddStringBinaryOp(string name, BinaryOp<string> op)
		{
			AddOpToNativeFunc (name, 2, ValueType.String, op);
		}

		static void AddListBinaryOp (string name, BinaryOp<InkList> op)
		{
			AddOpToNativeFunc (name, 2, ValueType.List, op);
		}

		static void AddListUnaryOp (string name, UnaryOp<InkList> op)
		{
			AddOpToNativeFunc (name, 1, ValueType.List, op);
		}

		static void AddFloatUnaryOp(string name, UnaryOp<float> op)
		{
			AddOpToNativeFunc (name, 1, ValueType.Float, op);
		}

		public override string ToString ()
		{
			return "Native '" + name + "'";
		}

		delegate object BinaryOp<T>(T left, T right);
		delegate object UnaryOp<T>(T val);

		NativeFunctionCall _prototype;
		bool _isPrototype;

		// Operations for each data type, for a single operation (e.g. "+")
		Dictionary<ValueType, object> _operationFuncs;

		static Dictionary<string, NativeFunctionCall> _nativeFunctions;
	}
 	public static class Json{
		public static List<T> JArrayToRuntimeObjList<T>(List<object> jArray, bool skipLast=false) where T : Runtime.Object
		{
			int count = jArray.Count;
			if (skipLast)
				count--;

			var list = new List<T> (jArray.Count);

			for (int i = 0; i < count; i++) {
				var jTok = jArray [i];
				var runtimeObj = JTokenToRuntimeObject (jTok) as T;
				list.Add (runtimeObj);
			}

			return list;
		}

		public static List<Runtime.Object> JArrayToRuntimeObjList(List<object> jArray, bool skipLast=false)
		{
			return JArrayToRuntimeObjList<Runtime.Object> (jArray, skipLast);
		}

		public static void WriteDictionaryRuntimeObjs(SimpleJson.Writer writer, Dictionary<string, Runtime.Object> dictionary) 
		{
			writer.WriteObjectStart();
			foreach(var keyVal in dictionary) {
				writer.WritePropertyStart(keyVal.Key);
				WriteRuntimeObject(writer, keyVal.Value);
				writer.WritePropertyEnd();
			}
			writer.WriteObjectEnd();
		}


		public static void WriteListRuntimeObjs(SimpleJson.Writer writer, List<Runtime.Object> list)
		{
			writer.WriteArrayStart();
			foreach (var val in list)
			{
				WriteRuntimeObject(writer, val);
			}
			writer.WriteArrayEnd();
		}

		public static void WriteIntDictionary(SimpleJson.Writer writer, Dictionary<string, int> dict)
		{
			writer.WriteObjectStart();
			foreach (var keyVal in dict)
				writer.WriteProperty(keyVal.Key, keyVal.Value);
			writer.WriteObjectEnd();
		}

		public static void WriteRuntimeObject(SimpleJson.Writer writer, Runtime.Object obj)
		{
			var container = obj as Container;
			if (container) {
				WriteRuntimeContainer(writer, container);
				return;
			}

			var divert = obj as Divert;
			if (divert)
			{
				string divTypeKey = "->";
				if (divert.isExternal)
					divTypeKey = "x()";
				else if (divert.pushesToStack)
				{
					if (divert.stackPushType == PushPopType.Function)
						divTypeKey = "f()";
					else if (divert.stackPushType == PushPopType.Tunnel)
						divTypeKey = "->t->";
				}

				string targetStr;
				if (divert.hasVariableTarget)
					targetStr = divert.variableDivertName;
				else
					targetStr = divert.targetPathString;

				writer.WriteObjectStart();

				writer.WriteProperty(divTypeKey, targetStr);

				if (divert.hasVariableTarget)
					writer.WriteProperty("var", true);

				if (divert.isConditional)
					writer.WriteProperty("c", true);

				if (divert.externalArgs > 0)
					writer.WriteProperty("exArgs", divert.externalArgs);

				writer.WriteObjectEnd();
				return;
			}

			var choicePoint = obj as ChoicePoint;
			if (choicePoint)
			{
				writer.WriteObjectStart();
				writer.WriteProperty("*", choicePoint.pathStringOnChoice);
				writer.WriteProperty("flg", choicePoint.flags);
				writer.WriteObjectEnd();
				return;
			}

			var boolVal = obj as BoolValue;
			if (boolVal) {
				writer.Write(boolVal.value);
				return;
			}

			var intVal = obj as IntValue;
			if (intVal) {
				writer.Write(intVal.value);
				return;
			}

			var floatVal = obj as FloatValue;
			if (floatVal) {
				writer.Write(floatVal.value);
				return;
			}

			var strVal = obj as StringValue;
			if (strVal)
			{
				if (strVal.isNewline)
					writer.Write("\\n", escape:false);
				else {
					writer.WriteStringStart();
					writer.WriteStringInner("^");
					writer.WriteStringInner(strVal.value);
					writer.WriteStringEnd();
				}
				return;
			}

			var listVal = obj as ListValue;
			if (listVal)
			{
				WriteInkList(writer, listVal);
				return;
			}

			var divTargetVal = obj as DivertTargetValue;
			if (divTargetVal)
			{
				writer.WriteObjectStart();
				writer.WriteProperty("^->", divTargetVal.value.componentsString);
				writer.WriteObjectEnd();
				return;
			}

			var varPtrVal = obj as VariablePointerValue;
			if (varPtrVal)
			{
				writer.WriteObjectStart();
				writer.WriteProperty("^var", varPtrVal.value);
				writer.WriteProperty("ci", varPtrVal.contextIndex);
				writer.WriteObjectEnd();
				return;
			}

			var glue = obj as Runtime.Glue;
			if (glue) {
				writer.Write("<>");
				return;
			}

			var controlCmd = obj as ControlCommand;
			if (controlCmd)
			{
				writer.Write(_controlCommandNames[(int)controlCmd.commandType]);
				return;
			}

			var nativeFunc = obj as Runtime.NativeFunctionCall;
			if (nativeFunc)
			{
				var name = nativeFunc.name;

				// Avoid collision with ^ used to indicate a string
				if (name == "^") name = "L^";

				writer.Write(name);
				return;
			}


			// Variable reference
			var varRef = obj as VariableReference;
			if (varRef)
			{
				writer.WriteObjectStart();

				string readCountPath = varRef.pathStringForCount;
				if (readCountPath != null)
				{
					writer.WriteProperty("CNT?", readCountPath);
				}
				else
				{
					writer.WriteProperty("VAR?", varRef.name);
				}

				writer.WriteObjectEnd();
				return;
			}

			// Variable assignment
			var varAss = obj as VariableAssignment;
			if (varAss)
			{
				writer.WriteObjectStart();

				string key = varAss.isGlobal ? "VAR=" : "temp=";
				writer.WriteProperty(key, varAss.variableName);

				// Reassignment?
				if (!varAss.isNewDeclaration)
					writer.WriteProperty("re", true);

				writer.WriteObjectEnd();

				return;
			}

			// Void
			var voidObj = obj as Void;
			if (voidObj) {
				writer.Write("void");
				return;
			}

			// Tag
			var tag = obj as Tag;
			if (tag)
			{
				writer.WriteObjectStart();
				writer.WriteProperty("#", tag.text);
				writer.WriteObjectEnd();
				return;
			}

			// Used when serialising save state only
			var choice = obj as Choice;
			if (choice) {
				WriteChoice(writer, choice);
				return;
			}

			throw new System.Exception("Failed to write runtime object to JSON: " + obj);
		}

		public static Dictionary<string, Runtime.Object> JObjectToDictionaryRuntimeObjs(Dictionary<string, object> jObject)
		{
			var dict = new Dictionary<string, Runtime.Object> (jObject.Count);

			foreach (var keyVal in jObject) {
				dict [keyVal.Key] = JTokenToRuntimeObject(keyVal.Value);
			}

			return dict;
		}

		public static Dictionary<string, int> JObjectToIntDictionary(Dictionary<string, object> jObject)
		{
			var dict = new Dictionary<string, int> (jObject.Count);
			foreach (var keyVal in jObject) {
				dict [keyVal.Key] = (int)keyVal.Value;
			}
			return dict;
		}

		// ----------------------
		// JSON ENCODING SCHEME
		// ----------------------
		//
		// Glue:           "<>", "G<", "G>"
		// 
		// ControlCommand: "ev", "out", "/ev", "du" "pop", "->->", "~ret", "str", "/str", "nop", 
		//                 "choiceCnt", "turns", "visit", "seq", "thread", "done", "end"
		// 
		// NativeFunction: "+", "-", "/", "*", "%" "~", "==", ">", "<", ">=", "<=", "!=", "!"... etc
		// 
		// Void:           "void"
		// 
		// Value:          "^string value", "^^string value beginning with ^"
		//                 5, 5.2
		//                 {"^->": "path.target"}
		//                 {"^var": "varname", "ci": 0}
		// 
		// Container:      [...]
		//                 [..., 
		//                     {
		//                         "subContainerName": ..., 
		//                         "#f": 5,                    // flags
		//                         "#n": "containerOwnName"    // only if not redundant
		//                     }
		//                 ]
		// 
		// Divert:         {"->": "path.target", "c": true }
		//                 {"->": "path.target", "var": true}
		//                 {"f()": "path.func"}
		//                 {"->t->": "path.tunnel"}
		//                 {"x()": "externalFuncName", "exArgs": 5}
		// 
		// Var Assign:     {"VAR=": "varName", "re": true}   // reassignment
		//                 {"temp=": "varName"}
		// 
		// Var ref:        {"VAR?": "varName"}
		//                 {"CNT?": "stitch name"}
		// 
		// ChoicePoint:    {"*": pathString,
		//                  "flg": 18 }
		//
		// Choice:         Nothing too clever, it's only used in the save state,
		//                 there's not likely to be many of them.
		// 
		// Tag:            {"#": "the tag text"}
		public static Runtime.Object JTokenToRuntimeObject(object token)
		{
			if (token is int || token is float || token is bool) {
				return Value.Create (token);
			}
			
			if (token is string) {
				string str = (string)token;

				// String value
				char firstChar = str[0];
				if (firstChar == '^')
					return new StringValue (str.Substring (1));
				else if( firstChar == '\n' && str.Length == 1)
					return new StringValue ("\n");

				// Glue
				if (str == "<>") return new Runtime.Glue ();

				// Control commands (would looking up in a hash set be faster?)
				for (int i = 0; i < _controlCommandNames.Length; ++i) {
					string cmdName = _controlCommandNames [i];
					if (str == cmdName) {
						return new Runtime.ControlCommand ((ControlCommand.CommandType)i);
					}
				}

				// Native functions
				// "^" conflicts with the way to identify strings, so now
				// we know it's not a string, we can convert back to the proper
				// symbol for the operator.
				if (str == "L^") str = "^";
				if( NativeFunctionCall.CallExistsWithName(str) )
					return NativeFunctionCall.CallWithName (str);

				// Pop
				if (str == "->->")
					return Runtime.ControlCommand.PopTunnel ();
				else if (str == "~ret")
					return Runtime.ControlCommand.PopFunction ();

				// Void
				if (str == "void")
					return new Runtime.Void ();
			}

			if (token is Dictionary<string, object>) {

				var obj = (Dictionary < string, object> )token;
				object propValue;

				// Divert target value to path
				if (obj.TryGetValue ("^->", out propValue))
					return new DivertTargetValue (new Path ((string)propValue));

				// VariablePointerValue
				if (obj.TryGetValue ("^var", out propValue)) {
					var varPtr = new VariablePointerValue ((string)propValue);
					if (obj.TryGetValue ("ci", out propValue))
						varPtr.contextIndex = (int)propValue;
					return varPtr;
				}

				// Divert
				bool isDivert = false;
				bool pushesToStack = false;
				PushPopType divPushType = PushPopType.Function;
				bool external = false;
				if (obj.TryGetValue ("->", out propValue)) {
					isDivert = true;
				}
				else if (obj.TryGetValue ("f()", out propValue)) {
					isDivert = true;
					pushesToStack = true;
					divPushType = PushPopType.Function;
				}
				else if (obj.TryGetValue ("->t->", out propValue)) {
					isDivert = true;
					pushesToStack = true;
					divPushType = PushPopType.Tunnel;
				}
				else if (obj.TryGetValue ("x()", out propValue)) {
					isDivert = true;
					external = true;
					pushesToStack = false;
					divPushType = PushPopType.Function;
				}
				if (isDivert) {
					var divert = new Divert ();
					divert.pushesToStack = pushesToStack;
					divert.stackPushType = divPushType;
					divert.isExternal = external;

					string target = propValue.ToString ();

					if (obj.TryGetValue ("var", out propValue))
						divert.variableDivertName = target;
					else
						divert.targetPathString = target;

					divert.isConditional = obj.TryGetValue("c", out propValue);

					if (external) {
						if (obj.TryGetValue ("exArgs", out propValue))
							divert.externalArgs = (int)propValue;
					}

					return divert;
				}
					
				// Choice
				if (obj.TryGetValue ("*", out propValue)) {
					var choice = new ChoicePoint ();
					choice.pathStringOnChoice = propValue.ToString();

					if (obj.TryGetValue ("flg", out propValue))
						choice.flags = (int)propValue;

					return choice;
				}

				// Variable reference
				if (obj.TryGetValue ("VAR?", out propValue)) {
					return new VariableReference (propValue.ToString ());
				} else if (obj.TryGetValue ("CNT?", out propValue)) {
					var readCountVarRef = new VariableReference ();
					readCountVarRef.pathStringForCount = propValue.ToString ();
					return readCountVarRef;
				}

				// Variable assignment
				bool isVarAss = false;
				bool isGlobalVar = false;
				if (obj.TryGetValue ("VAR=", out propValue)) {
					isVarAss = true;
					isGlobalVar = true;
				} else if (obj.TryGetValue ("temp=", out propValue)) {
					isVarAss = true;
					isGlobalVar = false;
				}
				if (isVarAss) {
					var varName = propValue.ToString ();
					var isNewDecl = !obj.TryGetValue("re", out propValue);
					var varAss = new VariableAssignment (varName, isNewDecl);
					varAss.isGlobal = isGlobalVar;
					return varAss;
				}

				// Tag
				if (obj.TryGetValue ("#", out propValue)) {
					return new Runtime.Tag ((string)propValue);
				}

				// List value
				if (obj.TryGetValue ("list", out propValue)) {
					var listContent = (Dictionary<string, object>)propValue;
					var rawList = new InkList ();
					if (obj.TryGetValue ("origins", out propValue)) {
						var namesAsObjs = (List<object>)propValue;
						rawList.SetInitialOriginNames (namesAsObjs.Cast<string>().ToList());
					}
					foreach (var nameToVal in listContent) {
						var item = new InkListItem (nameToVal.Key);
						var val = (int)nameToVal.Value;
						rawList.Add (item, val);
					}
					return new ListValue (rawList);
				}

				// Used when serialising save state only
				if (obj ["originalChoicePath"] != null)
					return JObjectToChoice (obj);
			}

			// Array is always a Runtime.Container
			if (token is List<object>) {
				return JArrayToContainer((List<object>)token);
			}

			if (token == null)
				return null;

			throw new System.Exception ("Failed to convert token to runtime object: " + token);
		}

		public static void WriteRuntimeContainer(SimpleJson.Writer writer, Container container, bool withoutName = false)
		{
			writer.WriteArrayStart();

			foreach (var c in container.content)
				WriteRuntimeObject(writer, c);

			// Container is always an array [...]
			// But the final element is always either:
			//  - a dictionary containing the named content, as well as possibly
			//    the key "#" with the count flags
			//  - null, if neither of the above
			var namedOnlyContent = container.namedOnlyContent;
			var countFlags = container.countFlags;
			var hasNameProperty = container.name != null && !withoutName;

			bool hasTerminator = namedOnlyContent != null || countFlags > 0 || hasNameProperty;

			if( hasTerminator )
				writer.WriteObjectStart();

			if ( namedOnlyContent != null ) {
				foreach(var namedContent in namedOnlyContent) {
					var name = namedContent.Key;
					var namedContainer = namedContent.Value as Container;
					writer.WritePropertyStart(name);
					WriteRuntimeContainer(writer, namedContainer, withoutName:true);
					writer.WritePropertyEnd();
				}
			}

			if (countFlags > 0)
				writer.WriteProperty("#f", countFlags);

			if (hasNameProperty)
				writer.WriteProperty("#n", container.name);

			if (hasTerminator)
				writer.WriteObjectEnd();
			else
				writer.WriteNull();

			writer.WriteArrayEnd();
		}

		static Container JArrayToContainer(List<object> jArray)
		{
			var container = new Container ();
			container.content = JArrayToRuntimeObjList (jArray, skipLast:true);

			// Final object in the array is always a combination of
			//  - named content
			//  - a "#f" key with the countFlags
			// (if either exists at all, otherwise null)
			var terminatingObj = jArray [jArray.Count - 1] as Dictionary<string, object>;
			if (terminatingObj != null) {

				var namedOnlyContent = new Dictionary<string, Runtime.Object> (terminatingObj.Count);

				foreach (var keyVal in terminatingObj) {
					if (keyVal.Key == "#f") {
						container.countFlags = (int)keyVal.Value;
					} else if (keyVal.Key == "#n") {
						container.name = keyVal.Value.ToString ();
					} else {
						var namedContentItem = JTokenToRuntimeObject(keyVal.Value);
						var namedSubContainer = namedContentItem as Container;
						if (namedSubContainer)
							namedSubContainer.name = keyVal.Key;
						namedOnlyContent [keyVal.Key] = namedContentItem;
					}
				}

				container.namedOnlyContent = namedOnlyContent;
			}

			return container;
		}

		static Choice JObjectToChoice(Dictionary<string, object> jObj)
		{
			var choice = new Choice();
			choice.text = jObj ["text"].ToString();
			choice.index = (int)jObj ["index"];
			choice.sourcePath = jObj ["originalChoicePath"].ToString();
			choice.originalThreadIndex = (int)jObj ["originalThreadIndex"];
			choice.pathStringOnChoice = jObj ["targetPath"].ToString();
			return choice;
		}
		public static void WriteChoice(SimpleJson.Writer writer, Choice choice)
		{
			writer.WriteObjectStart();
			writer.WriteProperty("text", choice.text);
			writer.WriteProperty("index", choice.index);
			writer.WriteProperty("originalChoicePath", choice.sourcePath);
			writer.WriteProperty("originalThreadIndex", choice.originalThreadIndex);
			writer.WriteProperty("targetPath", choice.pathStringOnChoice);
			writer.WriteObjectEnd();
		}

		static void WriteInkList(SimpleJson.Writer writer, ListValue listVal)
		{
			var rawList = listVal.value;

			writer.WriteObjectStart();

			writer.WritePropertyStart("list");

			writer.WriteObjectStart();

			foreach (var itemAndValue in rawList)
			{
				var item = itemAndValue.Key;
				int itemVal = itemAndValue.Value;

				writer.WritePropertyNameStart();
				writer.WritePropertyNameInner(item.originName ?? "?");
				writer.WritePropertyNameInner(".");
				writer.WritePropertyNameInner(item.itemName);
				writer.WritePropertyNameEnd();

				writer.Write(itemVal);

				writer.WritePropertyEnd();
			}

			writer.WriteObjectEnd();

			writer.WritePropertyEnd();

			if (rawList.Count == 0 && rawList.originNames != null && rawList.originNames.Count > 0)
			{
				writer.WritePropertyStart("origins");
				writer.WriteArrayStart();
				foreach (var name in rawList.originNames)
					writer.Write(name);
				writer.WriteArrayEnd();
				writer.WritePropertyEnd();
			}

			writer.WriteObjectEnd();
		}

		public static ListDefinitionsOrigin JTokenToListDefinitions (object obj)
		{
			var defsObj = (Dictionary<string, object>)obj;

			var allDefs = new List<ListDefinition> ();

			foreach (var kv in defsObj) {
				var name = (string) kv.Key;
				var listDefJson = (Dictionary<string, object>)kv.Value;

				// Cast (string, object) to (string, int) for items
				var items = new Dictionary<string, int> ();
				foreach (var nameValue in listDefJson)
					items.Add(nameValue.Key, (int)nameValue.Value);

				var def = new ListDefinition (name, items);
				allDefs.Add (def);
			}

			return new ListDefinitionsOrigin (allDefs);
		}

		static Json() 
		{
			_controlCommandNames = new string[(int)ControlCommand.CommandType.TOTAL_VALUES];

			_controlCommandNames [(int)ControlCommand.CommandType.EvalStart] = "ev";
			_controlCommandNames [(int)ControlCommand.CommandType.EvalOutput] = "out";
			_controlCommandNames [(int)ControlCommand.CommandType.EvalEnd] = "/ev";
			_controlCommandNames [(int)ControlCommand.CommandType.Duplicate] = "du";
			_controlCommandNames [(int)ControlCommand.CommandType.PopEvaluatedValue] = "pop";
			_controlCommandNames [(int)ControlCommand.CommandType.PopFunction] = "~ret";
			_controlCommandNames [(int)ControlCommand.CommandType.PopTunnel] = "->->";
			_controlCommandNames [(int)ControlCommand.CommandType.BeginString] = "str";
			_controlCommandNames [(int)ControlCommand.CommandType.EndString] = "/str";
			_controlCommandNames [(int)ControlCommand.CommandType.NoOp] = "nop";
			_controlCommandNames [(int)ControlCommand.CommandType.ChoiceCount] = "choiceCnt";
			_controlCommandNames [(int)ControlCommand.CommandType.Turns] = "turn";
			_controlCommandNames [(int)ControlCommand.CommandType.TurnsSince] = "turns";
			_controlCommandNames [(int)ControlCommand.CommandType.ReadCount] = "readc";
			_controlCommandNames [(int)ControlCommand.CommandType.Random] = "rnd";
			_controlCommandNames [(int)ControlCommand.CommandType.SeedRandom] = "srnd";
			_controlCommandNames [(int)ControlCommand.CommandType.VisitIndex] = "visit";
			_controlCommandNames [(int)ControlCommand.CommandType.SequenceShuffleIndex] = "seq";
			_controlCommandNames [(int)ControlCommand.CommandType.StartThread] = "thread";
			_controlCommandNames [(int)ControlCommand.CommandType.Done] = "done";
			_controlCommandNames [(int)ControlCommand.CommandType.End] = "end";
			_controlCommandNames [(int)ControlCommand.CommandType.ListFromInt] = "listInt";
			_controlCommandNames [(int)ControlCommand.CommandType.ListRange] = "range";
			_controlCommandNames [(int)ControlCommand.CommandType.ListRandom] = "lrnd";

			for (int i = 0; i < (int)ControlCommand.CommandType.TOTAL_VALUES; ++i) {
				if (_controlCommandNames [i] == null)
					throw new System.Exception ("Control command not accounted for in serialisation");
			}
		}

		static string[] _controlCommandNames;
	}
	public class CallStack{
		public class Element
		{
			public Pointer currentPointer;

			public bool inExpressionEvaluation;
			public Dictionary<string, Runtime.Object> temporaryVariables;
			public PushPopType type;

			// When this callstack element is actually a function evaluation called from the game,
			// we need to keep track of the size of the evaluation stack when it was called
			// so that we know whether there was any return value.
			public int evaluationStackHeightWhenPushed;

			// When functions are called, we trim whitespace from the start and end of what
			// they generate, so we make sure know where the function's start and end are.
			public int functionStartInOuputStream;

			public Element(PushPopType type, Pointer pointer, bool inExpressionEvaluation = false) {
				this.currentPointer = pointer;
				this.inExpressionEvaluation = inExpressionEvaluation;
				this.temporaryVariables = new Dictionary<string, Object>();
				this.type = type;
			}

			public Element Copy()
			{
				var copy = new Element (this.type, currentPointer, this.inExpressionEvaluation);
				copy.temporaryVariables = new Dictionary<string,Object>(this.temporaryVariables);
				copy.evaluationStackHeightWhenPushed = evaluationStackHeightWhenPushed;
				copy.functionStartInOuputStream = functionStartInOuputStream;
				return copy;
			}
		}

		public class Thread
		{
			public List<Element> callstack;
			public int threadIndex;
			public Pointer previousPointer;

			public Thread() {
				callstack = new List<Element>();
			}

			public Thread(Dictionary<string, object> jThreadObj, Story storyContext) : this() {
				threadIndex = (int) jThreadObj ["threadIndex"];

				List<object> jThreadCallstack = (List<object>) jThreadObj ["callstack"];
				foreach (object jElTok in jThreadCallstack) {

					var jElementObj = (Dictionary<string, object>)jElTok;

					PushPopType pushPopType = (PushPopType)(int)jElementObj ["type"];

					Pointer pointer = Pointer.Null;

					string currentContainerPathStr = null;
					object currentContainerPathStrToken;
					if (jElementObj.TryGetValue ("cPath", out currentContainerPathStrToken)) {
						currentContainerPathStr = currentContainerPathStrToken.ToString ();

						var threadPointerResult = storyContext.ContentAtPath (new Path (currentContainerPathStr));
						pointer.container = threadPointerResult.container;
						pointer.index = (int)jElementObj ["idx"];

						if (threadPointerResult.obj == null)
							throw new System.Exception ("When loading state, internal story location couldn't be found: " + currentContainerPathStr + ". Has the story changed since this save data was created?");
						else if (threadPointerResult.approximate)
							storyContext.Warning ("When loading state, exact internal story location couldn't be found: '" + currentContainerPathStr + "', so it was approximated to '"+pointer.container.path.ToString()+"' to recover. Has the story changed since this save data was created?");
					}

					bool inExpressionEvaluation = (bool)jElementObj ["exp"];

					var el = new Element (pushPopType, pointer, inExpressionEvaluation);

					object temps;
					if ( jElementObj.TryGetValue("temp", out temps) ) {
						el.temporaryVariables = Json.JObjectToDictionaryRuntimeObjs((Dictionary<string, object>)temps);
					} else {
						el.temporaryVariables.Clear();
					}					

					callstack.Add (el);
				}

				object prevContentObjPath;
				if( jThreadObj.TryGetValue("previousContentObject", out prevContentObjPath) ) {
					var prevPath = new Path((string)prevContentObjPath);
					previousPointer = storyContext.PointerAtPath(prevPath);
				}
			}

			public Thread Copy() {
				var copy = new Thread ();
				copy.threadIndex = threadIndex;
				foreach(var e in callstack) {
					copy.callstack.Add(e.Copy());
				}
				copy.previousPointer = previousPointer;
				return copy;
			}

			public void WriteJson(SimpleJson.Writer writer)
			{
				writer.WriteObjectStart();

				// callstack
				writer.WritePropertyStart("callstack");
				writer.WriteArrayStart();
				foreach (CallStack.Element el in callstack)
				{
					writer.WriteObjectStart();
					if(!el.currentPointer.isNull) {
						writer.WriteProperty("cPath", el.currentPointer.container.path.componentsString);
						writer.WriteProperty("idx", el.currentPointer.index);
					}

					writer.WriteProperty("exp", el.inExpressionEvaluation);
					writer.WriteProperty("type", (int)el.type);

					if(el.temporaryVariables.Count > 0) {
						writer.WritePropertyStart("temp");
						Json.WriteDictionaryRuntimeObjs(writer, el.temporaryVariables);
						writer.WritePropertyEnd();
					}

					writer.WriteObjectEnd();
				}
				writer.WriteArrayEnd();
				writer.WritePropertyEnd();

				// threadIndex
				writer.WriteProperty("threadIndex", threadIndex);

				if (!previousPointer.isNull)
				{
					writer.WriteProperty("previousContentObject", previousPointer.Resolve().path.ToString());
				}

				writer.WriteObjectEnd();
			}
		}

		public List<Element> elements {
			get {
				return callStack;
			}
		}

		public int depth {
			get {
				return elements.Count;
			}
		}

		public Element currentElement { 
			get {
				var thread = _threads [_threads.Count - 1];
				var cs = thread.callstack;
				return cs [cs.Count - 1];
			} 
		}

		public int currentElementIndex {
			get {
				return callStack.Count - 1;
			}
		}

		public Thread currentThread
		{
			get {
				return _threads [_threads.Count - 1];
			}
			set {
				Debug.Assert (_threads.Count == 1, "Shouldn't be directly setting the current thread when we have a stack of them");
				_threads.Clear ();
				_threads.Add (value);
			}
		}

		public bool canPop {
			get {
				return callStack.Count > 1;
			}
		}

		public CallStack (Story storyContext)
		{
			_startOfRoot = Pointer.StartOf(storyContext.rootContentContainer);
			Reset();
		}


		public CallStack(CallStack toCopy)
		{
			_threads = new List<Thread> ();
			foreach (var otherThread in toCopy._threads) {
				_threads.Add (otherThread.Copy ());
			}
			_threadCounter = toCopy._threadCounter;
			_startOfRoot = toCopy._startOfRoot;
		}

		public void Reset() 
		{
			_threads = new List<Thread>();
			_threads.Add(new Thread());

			_threads[0].callstack.Add(new Element(PushPopType.Tunnel, _startOfRoot));
		}


		// Unfortunately it's not possible to implement jsonToken since
		// the setter needs to take a Story as a context in order to
		// look up objects from paths for currentContainer within elements.
		public void SetJsonToken(Dictionary<string, object> jObject, Story storyContext)
		{
			_threads.Clear ();

			var jThreads = (List<object>) jObject ["threads"];

			foreach (object jThreadTok in jThreads) {
				var jThreadObj = (Dictionary<string, object>)jThreadTok;
				var thread = new Thread (jThreadObj, storyContext);
				_threads.Add (thread);
			}

			_threadCounter = (int)jObject ["threadCounter"];
			_startOfRoot = Pointer.StartOf(storyContext.rootContentContainer);
		}

		public void WriteJson(SimpleJson.Writer w)
		{
			w.WriteObject(writer =>
			{
				writer.WritePropertyStart("threads");
				{
					writer.WriteArrayStart();

					foreach (CallStack.Thread thread in _threads)
					{
						thread.WriteJson(writer);
					}

					writer.WriteArrayEnd();
				}
				writer.WritePropertyEnd();

				writer.WritePropertyStart("threadCounter");
				{
					writer.Write(_threadCounter);
				}
				writer.WritePropertyEnd();
			});
		
		}

		public void PushThread()
		{
			var newThread = currentThread.Copy ();
			_threadCounter++;
			newThread.threadIndex = _threadCounter;
			_threads.Add (newThread);
		}

		public Thread ForkThread()
		{
			var forkedThread = currentThread.Copy();
			_threadCounter++;
			forkedThread.threadIndex = _threadCounter;
			return forkedThread;
		}

		public void PopThread()
		{
			if (canPopThread) {
				_threads.Remove (currentThread);
			} else {
				throw new System.Exception("Can't pop thread");
			}
		}

		public bool canPopThread
		{
			get {
				return _threads.Count > 1 && !elementIsEvaluateFromGame;
			}
		}

		public bool elementIsEvaluateFromGame
		{
			get {
				return currentElement.type == PushPopType.FunctionEvaluationFromGame;
			}
		}

		public void Push(PushPopType type, int externalEvaluationStackHeight = 0, int outputStreamLengthWithPushed = 0)
		{
			// When pushing to callstack, maintain the current content path, but jump out of expressions by default
			var element = new Element (
				type, 
				currentElement.currentPointer,
				inExpressionEvaluation: false
			);

			element.evaluationStackHeightWhenPushed = externalEvaluationStackHeight;
			element.functionStartInOuputStream = outputStreamLengthWithPushed;

			callStack.Add (element);
		}

		public bool CanPop(PushPopType? type = null) {

			if (!canPop)
				return false;
			
			if (type == null)
				return true;
			
			return currentElement.type == type;
		}
			
		public void Pop(PushPopType? type = null)
		{
			if (CanPop (type)) {
				callStack.RemoveAt (callStack.Count - 1);
				return;
			} else {
				throw new System.Exception("Mismatched push/pop in Callstack");
			}
		}

		// Get variable value, dereferencing a variable pointer if necessary
		public Runtime.Object GetTemporaryVariableWithName(string name, int contextIndex = -1)
		{
			if (contextIndex == -1)
				contextIndex = currentElementIndex+1;
			
			Runtime.Object varValue = null;

			var contextElement = callStack [contextIndex-1];

			if (contextElement.temporaryVariables.TryGetValue (name, out varValue)) {
				return varValue;
			} else {
				return null;
			}
		}
			
		public void SetTemporaryVariable(string name, Runtime.Object value, bool declareNew, int contextIndex = -1)
		{
			if (contextIndex == -1)
				contextIndex = currentElementIndex+1;

			var contextElement = callStack [contextIndex-1];
			
			if (!declareNew && !contextElement.temporaryVariables.ContainsKey(name)) {
				throw new System.Exception ("Could not find temporary variable to set: " + name);
			}

			Runtime.Object oldValue;
			if( contextElement.temporaryVariables.TryGetValue(name, out oldValue) )
				ListValue.RetainListOriginsForAssignment (oldValue, value);

			contextElement.temporaryVariables [name] = value;
		}

		// Find the most appropriate context for this variable.
		// Are we referencing a temporary or global variable?
		// Note that the compiler will have warned us about possible conflicts,
		// so anything that happens here should be safe!
		public int ContextForVariableNamed(string name)
		{
			// Current temporary context?
			// (Shouldn't attempt to access contexts higher in the callstack.)
			if (currentElement.temporaryVariables.ContainsKey (name)) {
				return currentElementIndex+1;
			} 

			// Global
			else {
				return 0;
			}
		}
			
		public Thread ThreadWithIndex(int index)
		{
			return _threads.Find (t => t.threadIndex == index);
		}

		private List<Element> callStack
		{
			get {
				return currentThread.callstack;
			}
		}

		public string callStackTrace {
			get {
				var sb = new System.Text.StringBuilder();

				for(int t=0; t<_threads.Count; t++) {

					var thread = _threads[t];
					var isCurrent = (t == _threads.Count-1);
					sb.AppendFormat("=== THREAD {0}/{1} {2}===\n", (t+1), _threads.Count, (isCurrent ? "(current) ":""));

					for(int i=0; i<thread.callstack.Count; i++) {

						if( thread.callstack[i].type == PushPopType.Function )
							sb.Append("  [FUNCTION] ");
						else
							sb.Append("  [TUNNEL] ");

						var pointer = thread.callstack[i].currentPointer;
						if( !pointer.isNull ) {
							sb.Append("<SOMEWHERE IN ");
							sb.Append(pointer.container.path.ToString());
							sb.AppendLine(">");
						}
					}
				}


				return sb.ToString();
			}
		}

		List<Thread> _threads;
		int _threadCounter;
		Pointer _startOfRoot;
	}
	public class VariableAssignment : Object{
		public string variableName { get; protected set; }
		public bool isNewDeclaration { get; protected set; }
		public bool isGlobal { get; set; }

		public VariableAssignment (string variableName, bool isNewDeclaration)
		{
			this.variableName = variableName;
			this.isNewDeclaration = isNewDeclaration;
		}

		// Require default constructor for serialisation
		public VariableAssignment() : this(null, false) {}

		public override string ToString ()
		{
			return "VarAssign to " + variableName;
		}
	}
	public class VariablesState : IEnumerable<string> {
		public delegate void VariableChanged(string variableName, Runtime.Object newValue);
		public event VariableChanged variableChangedEvent;

		public StatePatch patch;

		public bool batchObservingVariableChanges 
		{ 
			get {
				return _batchObservingVariableChanges;
			}
			set { 
				_batchObservingVariableChanges = value;
				if (value) {
					_changedVariablesForBatchObs = new HashSet<string> ();
				} 

				// Finished observing variables in a batch - now send 
				// notifications for changed variables all in one go.
				else {
					if (_changedVariablesForBatchObs != null) {
						foreach (var variableName in _changedVariablesForBatchObs) {
							var currentValue = _globalVariables [variableName];
							variableChangedEvent (variableName, currentValue);
						}
					}

					_changedVariablesForBatchObs = null;
				}
			}
		}
		bool _batchObservingVariableChanges;

		// Allow StoryState to change the current callstack, e.g. for
		// temporary function evaluation.
		public CallStack callStack {
			get {
				return _callStack;
			}
			set {
				_callStack = value;
			}
		}

		/// <summary>
		/// Get or set the value of a named global ink variable.
		/// The types available are the standard ink types. Certain
		/// types will be implicitly casted when setting.
		/// For example, doubles to floats, longs to ints, and bools
		/// to ints.
		/// </summary>
		public object this[string variableName]
		{
			get {
				Runtime.Object varContents;

				if (patch != null && patch.TryGetGlobal(variableName, out varContents))
					return (varContents as Runtime.Value).valueObject;

				// Search main dictionary first.
				// If it's not found, it might be because the story content has changed,
				// and the original default value hasn't be instantiated.
				// Should really warn somehow, but it's difficult to see how...!
				if ( _globalVariables.TryGetValue (variableName, out varContents) || 
					 _defaultGlobalVariables.TryGetValue(variableName, out varContents) )
					return (varContents as Runtime.Value).valueObject;
				else {
					return null;
				}
			}
			set {
				if (!_defaultGlobalVariables.ContainsKey (variableName))
					throw new StoryException ("Cannot assign to a variable ("+variableName+") that hasn't been declared in the story");
				
				var val = Runtime.Value.Create(value);
				if (val == null) {
					if (value == null) {
						throw new Exception ("Cannot pass null to VariableState");
					} else {
						throw new Exception ("Invalid value passed to VariableState: "+value.ToString());
					}
				}

				SetGlobal (variableName, val);
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		/// <summary>
		/// Enumerator to allow iteration over all global variables by name.
		/// </summary>
		public IEnumerator<string> GetEnumerator()
		{
			return _globalVariables.Keys.GetEnumerator();
		}

		public VariablesState (CallStack callStack, ListDefinitionsOrigin listDefsOrigin)
		{
			_globalVariables = new Dictionary<string, Object> ();
			_callStack = callStack;
			_listDefsOrigin = listDefsOrigin;
		}

		public void ApplyPatch()
		{
			foreach(var namedVar in patch.globals) {
				_globalVariables[namedVar.Key] = namedVar.Value;
			}

			if(_changedVariablesForBatchObs != null ) {
				foreach (var name in patch.changedVariables)
					_changedVariablesForBatchObs.Add(name);
			}

			patch = null;
		}

		public void SetJsonToken(Dictionary<string, object> jToken)
		{
			_globalVariables.Clear();

			foreach (var varVal in _defaultGlobalVariables) {
				object loadedToken;
				if( jToken.TryGetValue(varVal.Key, out loadedToken) ) {
					_globalVariables[varVal.Key] = Json.JTokenToRuntimeObject(loadedToken);
				} else {
					_globalVariables[varVal.Key] = varVal.Value;
				}
			}
		}

		/// <summary>
		/// When saving out JSON state, we can skip saving global values that
		/// remain equal to the initial values that were declared in ink.
		/// This makes the save file (potentially) much smaller assuming that
		/// at least a portion of the globals haven't changed. However, it
		/// can also take marginally longer to save in the case that the 
		/// majority HAVE changed, since it has to compare all globals.
		/// It may also be useful to turn this off for testing worst case
		/// save timing.
		/// </summary>
		public static bool dontSaveDefaultValues = true;

		public void WriteJson(SimpleJson.Writer writer)
		{
			writer.WriteObjectStart();
			foreach (var keyVal in _globalVariables)
			{
				var name = keyVal.Key;
				var val = keyVal.Value;

				if(dontSaveDefaultValues) {
					// Don't write out values that are the same as the default global values
					Runtime.Object defaultVal;
					if (_defaultGlobalVariables.TryGetValue(name, out defaultVal))
					{
						if (RuntimeObjectsEqual(val, defaultVal))
							continue;
					}
				}


				writer.WritePropertyStart(name);
				Json.WriteRuntimeObject(writer, val);
				writer.WritePropertyEnd();
			}
			writer.WriteObjectEnd();
		}

		public bool RuntimeObjectsEqual(Runtime.Object obj1, Runtime.Object obj2)
		{
			if (obj1.GetType() != obj2.GetType()) return false;

			// Perform equality on int/float/bool manually to avoid boxing
			var boolVal = obj1 as BoolValue;
			if( boolVal != null ) {
				return boolVal.value == ((BoolValue)obj2).value;
			}

			var intVal = obj1 as IntValue;
			if( intVal != null ) {
				return intVal.value == ((IntValue)obj2).value;
			}

			var floatVal = obj1 as FloatValue;
			if (floatVal != null)
			{
				return floatVal.value == ((FloatValue)obj2).value;
			}

			// Other Value type (using proper Equals: list, string, divert path)
			var val1 = obj1 as Value;
			var val2 = obj2 as Value;
			if( val1 != null ) {
				return val1.valueObject.Equals(val2.valueObject);
			}

			throw new System.Exception("FastRoughDefinitelyEquals: Unsupported runtime object type: "+obj1.GetType());
		}

		public Runtime.Object GetVariableWithName(string name)
		{
			return GetVariableWithName (name, -1);
		}

		public Runtime.Object TryGetDefaultVariableValue (string name)
		{
			Runtime.Object val = null;
			_defaultGlobalVariables.TryGetValue (name, out val);
			return val;
		}

		public bool GlobalVariableExistsWithName(string name)
		{
			return _globalVariables.ContainsKey(name) || _defaultGlobalVariables != null && _defaultGlobalVariables.ContainsKey(name);
		}

		Runtime.Object GetVariableWithName(string name, int contextIndex)
		{
			Runtime.Object varValue = GetRawVariableWithName (name, contextIndex);

			// Get value from pointer?
			var varPointer = varValue as VariablePointerValue;
			if (varPointer) {
				varValue = ValueAtVariablePointer (varPointer);
			}

			return varValue;
		}

		Runtime.Object GetRawVariableWithName(string name, int contextIndex)
		{
			Runtime.Object varValue = null;

			// 0 context = global
			if (contextIndex == 0 || contextIndex == -1) {
				if (patch != null && patch.TryGetGlobal(name, out varValue))
					return varValue;

				if ( _globalVariables.TryGetValue (name, out varValue) )
					return varValue;

				// Getting variables can actually happen during globals set up since you can do
				//  VAR x = A_LIST_ITEM
				// So _defaultGlobalVariables may be null.
				// We need to do this check though in case a new global is added, so we need to
				// revert to the default globals dictionary since an initial value hasn't yet been set.
				if( _defaultGlobalVariables != null && _defaultGlobalVariables.TryGetValue(name, out varValue) ) {
					return varValue;
				}

				var listItemValue = _listDefsOrigin.FindSingleItemListWithName (name);
				if (listItemValue)
					return listItemValue;
			} 

			// Temporary
			varValue = _callStack.GetTemporaryVariableWithName (name, contextIndex);

			return varValue;
		}

		public Runtime.Object ValueAtVariablePointer(VariablePointerValue pointer)
		{
			return GetVariableWithName (pointer.variableName, pointer.contextIndex);
		}

		public void Assign(VariableAssignment varAss, Runtime.Object value)
		{
			var name = varAss.variableName;
			int contextIndex = -1;

			// Are we assigning to a global variable?
			bool setGlobal = false;
			if (varAss.isNewDeclaration) {
				setGlobal = varAss.isGlobal;
			} else {
				setGlobal = GlobalVariableExistsWithName (name);
			}

			// Constructing new variable pointer reference
			if (varAss.isNewDeclaration) {
				var varPointer = value as VariablePointerValue;
				if (varPointer) {
					var fullyResolvedVariablePointer = ResolveVariablePointer (varPointer);
					value = fullyResolvedVariablePointer;
				}

			} 

			// Assign to existing variable pointer?
			// Then assign to the variable that the pointer is pointing to by name.
			else {

				// De-reference variable reference to point to
				VariablePointerValue existingPointer = null;
				do {
					existingPointer = GetRawVariableWithName (name, contextIndex) as VariablePointerValue;
					if (existingPointer) {
						name = existingPointer.variableName;
						contextIndex = existingPointer.contextIndex;
						setGlobal = (contextIndex == 0);
					}
				} while(existingPointer);
			}


			if (setGlobal) {
				SetGlobal (name, value);
			} else {
				_callStack.SetTemporaryVariable (name, value, varAss.isNewDeclaration, contextIndex);
			}
		}

		public void SnapshotDefaultGlobals ()
		{
			_defaultGlobalVariables = new Dictionary<string, Object> (_globalVariables);
		}

		void RetainListOriginsForAssignment (Runtime.Object oldValue, Runtime.Object newValue)
		{
			var oldList = oldValue as ListValue;
			var newList = newValue as ListValue;
			if (oldList && newList && newList.value.Count == 0)
				newList.value.SetInitialOriginNames (oldList.value.originNames);
		}

		public void SetGlobal(string variableName, Runtime.Object value)
		{
			Runtime.Object oldValue = null;
			if( patch == null || !patch.TryGetGlobal(variableName, out oldValue) )
				_globalVariables.TryGetValue (variableName, out oldValue);

			ListValue.RetainListOriginsForAssignment (oldValue, value);

			if (patch != null)
				patch.SetGlobal(variableName, value);
			else
				_globalVariables [variableName] = value;

			if (variableChangedEvent != null && !value.Equals (oldValue)) {

				if (batchObservingVariableChanges) {
					if (patch != null)
						patch.AddChangedVariable(variableName);
					else if(_changedVariablesForBatchObs != null)
						_changedVariablesForBatchObs.Add (variableName);
				} else {
					variableChangedEvent (variableName, value);
				}
			}
		}

		// Given a variable pointer with just the name of the target known, resolve to a variable
		// pointer that more specifically points to the exact instance: whether it's global,
		// or the exact position of a temporary on the callstack.
		VariablePointerValue ResolveVariablePointer(VariablePointerValue varPointer)
		{
			int contextIndex = varPointer.contextIndex;

			if( contextIndex == -1 )
				contextIndex = GetContextIndexOfVariableNamed (varPointer.variableName);

			var valueOfVariablePointedTo = GetRawVariableWithName (varPointer.variableName, contextIndex);

			// Extra layer of indirection:
			// When accessing a pointer to a pointer (e.g. when calling nested or 
			// recursive functions that take a variable references, ensure we don't create
			// a chain of indirection by just returning the final target.
			var doubleRedirectionPointer = valueOfVariablePointedTo as VariablePointerValue;
			if (doubleRedirectionPointer) {
				return doubleRedirectionPointer;
			} 

			// Make copy of the variable pointer so we're not using the value direct from
			// the runtime. Temporary must be local to the current scope.
			else {
				return new VariablePointerValue (varPointer.variableName, contextIndex);
			}
		}

		// 0  if named variable is global
		// 1+ if named variable is a temporary in a particular call stack element
		int GetContextIndexOfVariableNamed(string varName)
		{
			if (GlobalVariableExistsWithName(varName))
				return 0;

			return _callStack.currentElementIndex;
		}

		Dictionary<string, Runtime.Object> _globalVariables;

		Dictionary<string, Runtime.Object> _defaultGlobalVariables;

		// Used for accessing temporary variables
		CallStack _callStack;
		HashSet<string> _changedVariablesForBatchObs;
		ListDefinitionsOrigin _listDefsOrigin;
	}
	public class DivertTargetValue : Value<Path>{
		public Path targetPath { get { return this.value; } set { this.value = value; } }
		public override ValueType valueType { get { return ValueType.DivertTarget; } }
		public override bool isTruthy { get { throw new System.Exception("Shouldn't be checking the truthiness of a divert target"); } }
			
		public DivertTargetValue(Path targetPath) : base(targetPath)
		{
		}

		public DivertTargetValue() : base(null)
		{}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType)
				return this;
			
			throw BadCastException (newType);
		}

		public override string ToString ()
		{
			return "DivertTargetValue(" + targetPath + ")";
		}
	}
	public class VariablePointerValue : Value<string>{
		public string variableName { get { return this.value; } set { this.value = value; } }
		public override ValueType valueType { get { return ValueType.VariablePointer; } }
		public override bool isTruthy { get { throw new System.Exception("Shouldn't be checking the truthiness of a variable pointer"); } }

		// Where the variable is located
		// -1 = default, unknown, yet to be determined
		// 0  = in global scope
		// 1+ = callstack element index + 1 (so that the first doesn't conflict with special global scope)
		public int contextIndex { get; set; }

		public VariablePointerValue(string variableName, int contextIndex = -1) : base(variableName)
		{
			this.contextIndex = contextIndex;
		}

		public VariablePointerValue() : this(null)
		{
		}

		public override Value Cast(ValueType newType)
		{
			if (newType == valueType)
				return this;

			throw BadCastException (newType);
		}

		public override string ToString ()
		{
			return "VariablePointerValue(" + variableName + ")";
		}

		public override Object Copy()
		{
			return new VariablePointerValue (variableName, contextIndex);
		}
	}
	public class Choice : Object{
		public string text { get; set; }
		public string pathStringOnChoice {
			get {
				return targetPath.ToString ();
			}
			set {
				targetPath = new Path (value);
			}
		}
		public string sourcePath;
		public int index { get; set; }

		public Path targetPath;

		public CallStack.Thread threadAtGeneration { get; set; }
		public int originalThreadIndex;

		public bool isInvisibleDefault;

		public Choice()
		{
		}
	}
	public class Flow {
		public string name;
		public CallStack callStack;
		public List<Runtime.Object> outputStream;
		public List<Choice> currentChoices;

		public Flow(string name, Story story) {
			this.name = name;
			this.callStack = new CallStack(story);
			this.outputStream = new List<Object>();
			this.currentChoices = new List<Choice>();
		}

		public Flow(string name, Story story, Dictionary<string, object> jObject) {
			this.name = name;
			this.callStack = new CallStack(story);
			this.callStack.SetJsonToken ((Dictionary < string, object > )jObject ["callstack"], story);
			this.outputStream = Json.JArrayToRuntimeObjList ((List<object>)jObject ["outputStream"]);
			this.currentChoices = Json.JArrayToRuntimeObjList<Choice>((List<object>)jObject ["currentChoices"]);

			// choiceThreads is optional
			object jChoiceThreadsObj;
			jObject.TryGetValue("choiceThreads", out jChoiceThreadsObj);
			LoadFlowChoiceThreads((Dictionary<string, object>)jChoiceThreadsObj, story);
		}

		public void WriteJson(SimpleJson.Writer writer)
		{
			writer.WriteObjectStart();

			writer.WriteProperty("callstack", callStack.WriteJson);
			writer.WriteProperty("outputStream", w => Json.WriteListRuntimeObjs(w, outputStream));

			// choiceThreads: optional
			// Has to come BEFORE the choices themselves are written out
			// since the originalThreadIndex of each choice needs to be set
			bool hasChoiceThreads = false;
			foreach (Choice c in currentChoices)
			{
				c.originalThreadIndex = c.threadAtGeneration.threadIndex;

				if (callStack.ThreadWithIndex(c.originalThreadIndex) == null)
				{
					if (!hasChoiceThreads)
					{
						hasChoiceThreads = true;
						writer.WritePropertyStart("choiceThreads");
						writer.WriteObjectStart();
					}

					writer.WritePropertyStart(c.originalThreadIndex);
					c.threadAtGeneration.WriteJson(writer);
					writer.WritePropertyEnd();
				}
			}

			if (hasChoiceThreads)
			{
				writer.WriteObjectEnd();
				writer.WritePropertyEnd();
			}


			writer.WriteProperty("currentChoices", w => {
				w.WriteArrayStart();
				foreach (var c in currentChoices)
					Json.WriteChoice(w, c);
				w.WriteArrayEnd();
			});


			writer.WriteObjectEnd();
		}

		// Used both to load old format and current
		public void LoadFlowChoiceThreads(Dictionary<string, object> jChoiceThreads, Story story)
		{
			foreach (var choice in currentChoices) {
				var foundActiveThread = callStack.ThreadWithIndex(choice.originalThreadIndex);
				if( foundActiveThread != null ) {
					choice.threadAtGeneration = foundActiveThread.Copy ();
				} else {
					var jSavedChoiceThread = (Dictionary <string, object>) jChoiceThreads[choice.originalThreadIndex.ToString()];
					choice.threadAtGeneration = new CallStack.Thread(jSavedChoiceThread, story);
				}
			}
		}
	}
	public class Tag : Runtime.Object{
		public string text { get; private set; }

		public Tag (string tagText)
		{
			this.text = tagText;
		}

		public override string ToString ()
		{
			return "# " + text;
		}
	}
 	public class StoryState{
		/// <summary>
		/// The current version of the state save file JSON-based format.
		/// </summary>
		public const int kInkSaveStateVersion = 9; // new: multi-flows, but backward compatible
		const int kMinCompatibleLoadVersion = 8;

		/// <summary>
		/// Callback for when a state is loaded
		/// </summary>
		public event Action onDidLoadState;

		/// <summary>
		/// Exports the current state to json format, in order to save the game.
		/// </summary>
		/// <returns>The save state in json format.</returns>
		public string ToJson() {
			var writer = new SimpleJson.Writer();
			WriteJson(writer);
			return writer.ToString();
		}

		/// <summary>
		/// Exports the current state to json format, in order to save the game.
		/// For this overload you can pass in a custom stream, such as a FileStream.
		/// </summary>
		public void ToJson(Stream stream) {
			var writer = new SimpleJson.Writer(stream);
			WriteJson(writer);
		}

		/// <summary>
		/// Loads a previously saved state in JSON format.
		/// </summary>
		/// <param name="json">The JSON string to load.</param>
		public void LoadJson(string json)
		{
			var jObject = SimpleJson.TextToDictionary (json);
			LoadJsonObj(jObject);
			if(onDidLoadState != null) onDidLoadState();
		}

		/// <summary>
		/// Gets the visit/read count of a particular Container at the given path.
		/// For a knot or stitch, that path string will be in the form:
		/// 
		///     knot
		///     knot.stitch
		/// 
		/// </summary>
		/// <returns>The number of times the specific knot or stitch has
		/// been enountered by the ink engine.</returns>
		/// <param name="pathString">The dot-separated path string of
		/// the specific knot or stitch.</param>
		public int VisitCountAtPathString(string pathString)
		{
			int visitCountOut;

			if ( _patch != null ) {
				var container = story.ContentAtPath(new Path(pathString)).container;
				if (container == null)
					throw new Exception("Content at path not found: " + pathString);

				if( _patch.TryGetVisitCount(container, out visitCountOut) )
					return visitCountOut;
			}

			if (_visitCounts.TryGetValue(pathString, out visitCountOut))
				return visitCountOut;

			return 0;
		}

		public int VisitCountForContainer(Container container)
		{
			if (!container.visitsShouldBeCounted)
			{
				story.Error("Read count for target (" + container.name + " - on " + container.debugMetadata + ") unknown.");
				return 0;
			}

			int count = 0;
			if (_patch != null && _patch.TryGetVisitCount(container, out count))
				return count;
				
			var containerPathStr = container.path.ToString();
			_visitCounts.TryGetValue(containerPathStr, out count);
			return count;
		}

		public void IncrementVisitCountForContainer(Container container)
		{
			if( _patch != null ) {
				var currCount = VisitCountForContainer(container);
				currCount++;
				_patch.SetVisitCount(container, currCount);
				return;
			}

			int count = 0;
			var containerPathStr = container.path.ToString();
			_visitCounts.TryGetValue(containerPathStr, out count);
			count++;
			_visitCounts[containerPathStr] = count;
		}

		public void RecordTurnIndexVisitToContainer(Container container)
		{
			if( _patch != null ) {
				_patch.SetTurnIndex(container, currentTurnIndex);
				return;
			}

			var containerPathStr = container.path.ToString();
			_turnIndices[containerPathStr] = currentTurnIndex;
		}

		public int TurnsSinceForContainer(Container container)
		{
			if (!container.turnIndexShouldBeCounted)
			{
				story.Error("TURNS_SINCE() for target (" + container.name + " - on " + container.debugMetadata + ") unknown.");
			}

			int index = 0;

			if ( _patch != null && _patch.TryGetTurnIndex(container, out index) ) {
				return currentTurnIndex - index;
			}

			var containerPathStr = container.path.ToString();
			if (_turnIndices.TryGetValue(containerPathStr, out index))
			{
				return currentTurnIndex - index;
			}
			else
			{
				return -1;
			}
		}

		public int callstackDepth {
			get {
				return callStack.depth;
			}
		}

		// REMEMBER! REMEMBER! REMEMBER!
		// When adding state, update the Copy method, and serialisation.
		// REMEMBER! REMEMBER! REMEMBER!

		public List<Runtime.Object> outputStream { 
			get { 
				return _currentFlow.outputStream; 
			} 
		}

		

		public List<Choice> currentChoices { 
			get { 
				// If we can continue generating text content rather than choices,
				// then we reflect the choice list as being empty, since choices
				// should always come at the end.
				if( canContinue ) return new List<Choice>();
				return _currentFlow.currentChoices;
			} 
		}
		public List<Choice> generatedChoices {
			get {
				return _currentFlow.currentChoices;
			}
		}

		// TODO: Consider removing currentErrors / currentWarnings altogether
		// and relying on client error handler code immediately handling StoryExceptions etc
		// Or is there a specific reason we need to collect potentially multiple
		// errors before throwing/exiting?
		public List<string> currentErrors { get; private set; }
		public List<string> currentWarnings { get; private set; }
		public VariablesState variablesState { get; private set; }
		public CallStack callStack { 
			get { 
				return _currentFlow.callStack;
			}
			// set {
			//     _currentFlow.callStack = value;
			// } 
		}

		public List<Runtime.Object> evaluationStack { get; private set; }
		public Pointer divertedPointer { get; set; }

		public int currentTurnIndex { get; private set; }
		public int storySeed { get; set; }
		public int previousRandom { get; set; }
		public bool didSafeExit { get; set; }

		public Story story { get; set; }

		/// <summary>
		/// String representation of the location where the story currently is.
		/// </summary>
		public string currentPathString {
			get {
				var pointer = currentPointer;
				if (pointer.isNull)
					return null;
				else
					return pointer.path.ToString();
			}
		}

		public Runtime.Pointer currentPointer {
			get {
				return callStack.currentElement.currentPointer;
			}
			set {
				callStack.currentElement.currentPointer = value;
			}
		}

		public Pointer previousPointer { 
			get {
				return callStack.currentThread.previousPointer;
			}
			set {
				callStack.currentThread.previousPointer = value;
			}
		}

		public bool canContinue {
			get {
				return !currentPointer.isNull && !hasError;
			}
		}
			
		public bool hasError
		{
			get {
				return currentErrors != null && currentErrors.Count > 0;
			}
		}

		public bool hasWarning {
			get {
				return currentWarnings != null && currentWarnings.Count > 0;
			}
		}

		public string currentText
		{
			get 
			{
				if( _outputStreamTextDirty ) {
					var sb = new StringBuilder ();

					foreach (var outputObj in outputStream) {
						var textContent = outputObj as StringValue;
						if (textContent != null) {
							sb.Append(textContent.value);
						}
					}

					_currentText = CleanOutputWhitespace (sb.ToString ());

					_outputStreamTextDirty = false;
				}

				return _currentText;
			}
		}
		string _currentText;

		// Cleans inline whitespace in the following way:
		//  - Removes all whitespace from the start and end of line (including just before a \n)
		//  - Turns all consecutive space and tab runs into single spaces (HTML style)
		string CleanOutputWhitespace(string str)
		{
			var sb = new StringBuilder(str.Length);

			int currentWhitespaceStart = -1;
			int startOfLine = 0;

			for (int i = 0; i < str.Length; i++) {
				var c = str[i];

				bool isInlineWhitespace = c == ' ' || c == '\t';

				if (isInlineWhitespace && currentWhitespaceStart == -1)
					currentWhitespaceStart = i;

				if (!isInlineWhitespace) {
					if (c != '\n' && currentWhitespaceStart > 0 && currentWhitespaceStart != startOfLine) {
						sb.Append(' ');
					}
					currentWhitespaceStart = -1;
				}

				if (c == '\n')
					startOfLine = i + 1;

				if (!isInlineWhitespace)
					sb.Append(c);
			}

			return sb.ToString();
		}

		public List<string> currentTags 
		{
			get 
			{
				if( _outputStreamTagsDirty ) {
					_currentTags = new List<string>();

					foreach (var outputObj in outputStream) {
						var tag = outputObj as Tag;
						if (tag != null) {
							_currentTags.Add (tag.text);
						}
					}

					_outputStreamTagsDirty = false;
				}

				return _currentTags;
			}
		}
		List<string> _currentTags;

		public string currentFlowName {
			get {
				return _currentFlow.name;
			}
		}

		public bool inExpressionEvaluation {
			get {
				return callStack.currentElement.inExpressionEvaluation;
			}
			set {
				callStack.currentElement.inExpressionEvaluation = value;
			}
		}
			
		public StoryState (Story story)
		{
			this.story = story;

			_currentFlow = new Flow(kDefaultFlowName, story);
			
			OutputStreamDirty();

			evaluationStack = new List<Runtime.Object> ();

			variablesState = new VariablesState (callStack, story.listDefinitions);

			_visitCounts = new Dictionary<string, int> ();
			_turnIndices = new Dictionary<string, int> ();

			currentTurnIndex = -1;

			// Seed the shuffle random numbers
			int timeSeed = DateTime.Now.Millisecond;
			storySeed = (new Random (timeSeed)).Next () % 100;
			previousRandom = 0;

			

			GoToStart();
		}

		public void GoToStart()
		{
			callStack.currentElement.currentPointer = Pointer.StartOf (story.mainContentContainer);
		}

		internal void SwitchFlow_Internal(string flowName)
		{
			if(flowName == null) throw new System.Exception("Must pass a non-null string to Story.SwitchFlow");
			
			if( _namedFlows == null ) {
				_namedFlows = new Dictionary<string, Flow>();
				_namedFlows[kDefaultFlowName] = _currentFlow;
			}

			if( flowName == _currentFlow.name ) {
				return;
			}

			Flow flow;
			if( !_namedFlows.TryGetValue(flowName, out flow) ) {
				flow = new Flow(flowName, story);
				_namedFlows[flowName] = flow;
			}

			_currentFlow = flow;
			variablesState.callStack = _currentFlow.callStack;

			// Cause text to be regenerated from output stream if necessary
			OutputStreamDirty();
		}

		internal void SwitchToDefaultFlow_Internal()
		{
			if( _namedFlows == null ) return;
			SwitchFlow_Internal(kDefaultFlowName);
		}

		internal void RemoveFlow_Internal(string flowName)
		{
			if(flowName == null) throw new System.Exception("Must pass a non-null string to Story.DestroyFlow");
			if(flowName == kDefaultFlowName) throw new System.Exception("Cannot destroy default flow");

			// If we're currently in the flow that's being removed, switch back to default
			if( _currentFlow.name == flowName ) {
				SwitchToDefaultFlow_Internal();
			}

			_namedFlows.Remove(flowName);
		}

		// Warning: Any Runtime.Object content referenced within the StoryState will
		// be re-referenced rather than cloned. This is generally okay though since
		// Runtime.Objects are treated as immutable after they've been set up.
		// (e.g. we don't edit a Runtime.StringValue after it's been created an added.)
		// I wonder if there's a sensible way to enforce that..??
		public StoryState CopyAndStartPatching()
		{
			var copy = new StoryState(story);

			copy._patch = new StatePatch(_patch);

			// Hijack the new default flow to become a copy of our current one
			// If the patch is applied, then this new flow will replace the old one in _namedFlows
			copy._currentFlow.name = _currentFlow.name;
			copy._currentFlow.callStack = new CallStack (_currentFlow.callStack);
			copy._currentFlow.currentChoices.AddRange(_currentFlow.currentChoices);
			copy._currentFlow.outputStream.AddRange(_currentFlow.outputStream);
			copy.OutputStreamDirty();

			// The copy of the state has its own copy of the named flows dictionary,
			// except with the current flow replaced with the copy above
			// (Assuming we're in multi-flow mode at all. If we're not then
			// the above copy is simply the default flow copy and we're done)
			if( _namedFlows != null ) {
				copy._namedFlows = new Dictionary<string, Flow>();
				foreach(var namedFlow in _namedFlows)
					copy._namedFlows[namedFlow.Key] = namedFlow.Value;
				copy._namedFlows[_currentFlow.name] = copy._currentFlow;
			}

			if (hasError) {
				copy.currentErrors = new List<string> ();
				copy.currentErrors.AddRange (currentErrors); 
			}
			if (hasWarning) {
				copy.currentWarnings = new List<string> ();
				copy.currentWarnings.AddRange (currentWarnings); 
			}

			
			// ref copy - exactly the same variables state!
			// we're expecting not to read it only while in patch mode
			// (though the callstack will be modified)
			copy.variablesState = variablesState;
			copy.variablesState.callStack = copy.callStack;
			copy.variablesState.patch = copy._patch;

			copy.evaluationStack.AddRange (evaluationStack);

			if (!divertedPointer.isNull)
				copy.divertedPointer = divertedPointer;

			copy.previousPointer = previousPointer;

			// visit counts and turn indicies will be read only, not modified
			// while in patch mode
			copy._visitCounts = _visitCounts;
			copy._turnIndices = _turnIndices;

			copy.currentTurnIndex = currentTurnIndex;
			copy.storySeed = storySeed;
			copy.previousRandom = previousRandom;

			copy.didSafeExit = didSafeExit;

			return copy;
		}

		public void RestoreAfterPatch()
		{
			// VariablesState was being borrowed by the patched
			// state, so restore it with our own callstack.
			// _patch will be null normally, but if you're in the
			// middle of a save, it may contain a _patch for save purpsoes.
			variablesState.callStack = callStack;
			variablesState.patch = _patch; // usually null
		}

		public void ApplyAnyPatch()
		{
			if (_patch == null) return;

			variablesState.ApplyPatch();

			foreach(var pathToCount in _patch.visitCounts)
				ApplyCountChanges(pathToCount.Key, pathToCount.Value, isVisit:true);

			foreach (var pathToIndex in _patch.turnIndices)
				ApplyCountChanges(pathToIndex.Key, pathToIndex.Value, isVisit:false);

			_patch = null;
		}

		void ApplyCountChanges(Container container, int newCount, bool isVisit)
		{
			var counts = isVisit ? _visitCounts : _turnIndices;
			counts[container.path.ToString()] = newCount;
		}

		void WriteJson(SimpleJson.Writer writer)
		{
			writer.WriteObjectStart();

			// Flows
			writer.WritePropertyStart("flows");
			writer.WriteObjectStart();

			// Multi-flow
			if( _namedFlows != null ) {
				foreach(var namedFlow in _namedFlows) {
					writer.WriteProperty(namedFlow.Key, namedFlow.Value.WriteJson);
				}
			} 
			
			// Single flow
			else {
				writer.WriteProperty(_currentFlow.name, _currentFlow.WriteJson);
			}

			writer.WriteObjectEnd();
			writer.WritePropertyEnd(); // end of flows

			writer.WriteProperty("currentFlowName", _currentFlow.name);

			writer.WriteProperty("variablesState", variablesState.WriteJson);

			writer.WriteProperty("evalStack", w => Json.WriteListRuntimeObjs(w, evaluationStack));


			if (!divertedPointer.isNull)
				writer.WriteProperty("currentDivertTarget", divertedPointer.path.componentsString);
				
			writer.WriteProperty("visitCounts", w => Json.WriteIntDictionary(w, _visitCounts));
			writer.WriteProperty("turnIndices", w => Json.WriteIntDictionary(w, _turnIndices));


			writer.WriteProperty("turnIdx", currentTurnIndex);
			writer.WriteProperty("storySeed", storySeed);
			writer.WriteProperty("previousRandom", previousRandom);

			writer.WriteProperty("inkSaveVersion", kInkSaveStateVersion);

			// Not using this right now, but could do in future.
			writer.WriteProperty("inkFormatVersion", Story.inkVersionCurrent);

			writer.WriteObjectEnd();
		}


		void LoadJsonObj(Dictionary<string, object> jObject)
		{
			object jSaveVersion = null;
			if (!jObject.TryGetValue("inkSaveVersion", out jSaveVersion)) {
				throw new Exception ("ink save format incorrect, can't load.");
			}
			else if ((int)jSaveVersion < kMinCompatibleLoadVersion) {
				throw new Exception("Ink save format isn't compatible with the current version (saw '"+jSaveVersion+"', but minimum is "+kMinCompatibleLoadVersion+"), so can't load.");
			}

			// Flows: Always exists in latest format (even if there's just one default)
			// but this dictionary doesn't exist in prev format
			object flowsObj = null;
			if (jObject.TryGetValue("flows", out flowsObj)) {
				var flowsObjDict = (Dictionary<string, object>)flowsObj;
				
				// Single default flow
				if( flowsObjDict.Count == 1 )
					_namedFlows = null;

				// Multi-flow, need to create flows dict
				else if( _namedFlows == null )
					_namedFlows = new Dictionary<string, Flow>();

				// Multi-flow, already have a flows dict
				else
					_namedFlows.Clear();

				// Load up each flow (there may only be one)
				foreach(var namedFlowObj in flowsObjDict) {
					var name = namedFlowObj.Key;
					var flowObj = (Dictionary<string, object>)namedFlowObj.Value;

					// Load up this flow using JSON data
					var flow = new Flow(name, story, flowObj);

					if( flowsObjDict.Count == 1 ) {
						_currentFlow = new Flow(name, story, flowObj);
					} else {
						_namedFlows[name] = flow;
					}
				}

				if( _namedFlows != null && _namedFlows.Count > 1 ) {
					var currFlowName = (string)jObject["currentFlowName"];
					_currentFlow = _namedFlows[currFlowName];
				}
			}

			// Old format: individually load up callstack, output stream, choices in current/default flow
			else {
				_namedFlows = null;
				_currentFlow.name = kDefaultFlowName;
				_currentFlow.callStack.SetJsonToken ((Dictionary < string, object > )jObject ["callstackThreads"], story);
				_currentFlow.outputStream = Json.JArrayToRuntimeObjList ((List<object>)jObject ["outputStream"]);
				_currentFlow.currentChoices = Json.JArrayToRuntimeObjList<Choice>((List<object>)jObject ["currentChoices"]);

				object jChoiceThreadsObj = null;
				jObject.TryGetValue("choiceThreads", out jChoiceThreadsObj);
				_currentFlow.LoadFlowChoiceThreads((Dictionary<string, object>)jChoiceThreadsObj, story);
			}

			OutputStreamDirty();

			variablesState.SetJsonToken((Dictionary < string, object> )jObject["variablesState"]);
			variablesState.callStack = _currentFlow.callStack;

			evaluationStack = Json.JArrayToRuntimeObjList ((List<object>)jObject ["evalStack"]);


			object currentDivertTargetPath;
			if (jObject.TryGetValue("currentDivertTarget", out currentDivertTargetPath)) {
				var divertPath = new Path (currentDivertTargetPath.ToString ());
				divertedPointer = story.PointerAtPath (divertPath);
			}
				
			_visitCounts = Json.JObjectToIntDictionary((Dictionary<string, object>)jObject["visitCounts"]);
			_turnIndices = Json.JObjectToIntDictionary((Dictionary<string, object>)jObject["turnIndices"]);

			currentTurnIndex = (int)jObject ["turnIdx"];
			storySeed = (int)jObject ["storySeed"];

			// Not optional, but bug in inkjs means it's actually missing in inkjs saves
			object previousRandomObj = null;
			if( jObject.TryGetValue("previousRandom", out previousRandomObj) ) {
				previousRandom = (int)previousRandomObj;
			} else {
				previousRandom = 0;
			}
		}
			
		public void ResetErrors()
		{
			currentErrors = null;
			currentWarnings = null;
		}
			
		public void ResetOutput(List<Runtime.Object> objs = null)
		{
			outputStream.Clear ();
			if( objs != null ) outputStream.AddRange (objs);
			OutputStreamDirty();
		}

		// Push to output stream, but split out newlines in text for consistency
		// in dealing with them later.
		public void PushToOutputStream(Runtime.Object obj)
		{
			var text = obj as StringValue;
			if (text) {
				var listText = TrySplittingHeadTailWhitespace (text);
				if (listText != null) {
					foreach (var textObj in listText) {
						PushToOutputStreamIndividual (textObj);
					}
					OutputStreamDirty();
					return;
				}
			}

			PushToOutputStreamIndividual (obj);

			OutputStreamDirty();
		}

		public void PopFromOutputStream (int count)
		{
			outputStream.RemoveRange (outputStream.Count - count, count);
			OutputStreamDirty ();
		}


		// At both the start and the end of the string, split out the new lines like so:
		//
		//  "   \n  \n     \n  the string \n is awesome \n     \n     "
		//      ^-----------^                           ^-------^
		// 
		// Excess newlines are converted into single newlines, and spaces discarded.
		// Outside spaces are significant and retained. "Interior" newlines within 
		// the main string are ignored, since this is for the purpose of gluing only.
		//
		//  - If no splitting is necessary, null is returned.
		//  - A newline on its own is returned in a list for consistency.
		List<Runtime.StringValue> TrySplittingHeadTailWhitespace(Runtime.StringValue single)
		{
			string str = single.value;

			int headFirstNewlineIdx = -1;
			int headLastNewlineIdx = -1;
			for (int i = 0; i < str.Length; i++) {
				char c = str [i];
				if (c == '\n') {
					if (headFirstNewlineIdx == -1)
						headFirstNewlineIdx = i;
					headLastNewlineIdx = i;
				}
				else if (c == ' ' || c == '\t')
					continue;
				else
					break;
			}

			int tailLastNewlineIdx = -1;
			int tailFirstNewlineIdx = -1;
			for (int i = str.Length-1; i >= 0; i--) {
				char c = str [i];
				if (c == '\n') {
					if (tailLastNewlineIdx == -1)
						tailLastNewlineIdx = i;
					tailFirstNewlineIdx = i;
				}
				else if (c == ' ' || c == '\t')
					continue;
				else
					break;
			}

			// No splitting to be done?
			if (headFirstNewlineIdx == -1 && tailLastNewlineIdx == -1)
				return null;
				
			var listTexts = new List<Runtime.StringValue> ();
			int innerStrStart = 0;
			int innerStrEnd = str.Length;

			if (headFirstNewlineIdx != -1) {
				if (headFirstNewlineIdx > 0) {
					var leadingSpaces = new StringValue (str.Substring (0, headFirstNewlineIdx));
					listTexts.Add(leadingSpaces);
				}
				listTexts.Add (new StringValue ("\n"));
				innerStrStart = headLastNewlineIdx + 1;
			}

			if (tailLastNewlineIdx != -1) {
				innerStrEnd = tailFirstNewlineIdx;
			}

			if (innerStrEnd > innerStrStart) {
				var innerStrText = str.Substring (innerStrStart, innerStrEnd - innerStrStart);
				listTexts.Add (new StringValue (innerStrText));
			}

			if (tailLastNewlineIdx != -1 && tailFirstNewlineIdx > headLastNewlineIdx) {
				listTexts.Add (new StringValue ("\n"));
				if (tailLastNewlineIdx < str.Length - 1) {
					int numSpaces = (str.Length - tailLastNewlineIdx) - 1;
					var trailingSpaces = new StringValue (str.Substring (tailLastNewlineIdx + 1, numSpaces));
					listTexts.Add(trailingSpaces);
				}
			}

			return listTexts;
		}

		void PushToOutputStreamIndividual(Runtime.Object obj)
		{
			var glue = obj as Runtime.Glue;
			var text = obj as Runtime.StringValue;

			bool includeInOutput = true;

			// New glue, so chomp away any whitespace from the end of the stream
			if (glue) {
				TrimNewlinesFromOutputStream();
				includeInOutput = true;
			}

			// New text: do we really want to append it, if it's whitespace?
			// Two different reasons for whitespace to be thrown away:
			//   - Function start/end trimming
			//   - User defined glue: <>
			// We also need to know when to stop trimming, when there's non-whitespace.
			else if( text ) {

				// Where does the current function call begin?
				var functionTrimIndex = -1;
				var currEl = callStack.currentElement;
				if (currEl.type == PushPopType.Function) {
					functionTrimIndex = currEl.functionStartInOuputStream;
				}

				// Do 2 things:
				//  - Find latest glue
				//  - Check whether we're in the middle of string evaluation
				// If we're in string eval within the current function, we
				// don't want to trim back further than the length of the current string.
				int glueTrimIndex = -1;
				for (int i = outputStream.Count - 1; i >= 0; i--) {
					var o = outputStream [i];
					var c = o as ControlCommand;
					var g = o as Glue;

					// Find latest glue
					if (g) {
						glueTrimIndex = i;
						break;
					} 

					// Don't function-trim past the start of a string evaluation section
					else if (c && c.commandType == ControlCommand.CommandType.BeginString) {
						if (i >= functionTrimIndex) {
							functionTrimIndex = -1;
						}
						break;
					}
				}

				// Where is the most agressive (earliest) trim point?
				var trimIndex = -1;
				if (glueTrimIndex != -1 && functionTrimIndex != -1)
					trimIndex = Math.Min (functionTrimIndex, glueTrimIndex);
				else if (glueTrimIndex != -1)
					trimIndex = glueTrimIndex;
				else
					trimIndex = functionTrimIndex;

				// So, are we trimming then?
				if (trimIndex != -1) {

					// While trimming, we want to throw all newlines away,
					// whether due to glue or the start of a function
					if (text.isNewline) {
						includeInOutput = false;
					} 

					// Able to completely reset when normal text is pushed
					else if (text.isNonWhitespace) {

						if( glueTrimIndex > -1 )
							RemoveExistingGlue ();

						// Tell all functions in callstack that we have seen proper text,
						// so trimming whitespace at the start is done.
						if (functionTrimIndex > -1) {
							var callstackElements = callStack.elements;
							for (int i = callstackElements.Count - 1; i >= 0; i--) {
								var el = callstackElements [i];
								if (el.type == PushPopType.Function) {
									el.functionStartInOuputStream = -1;
								} else {
									break;
								}
							}
						}
					}
				} 

				// De-duplicate newlines, and don't ever lead with a newline
				else if (text.isNewline) {
					if (outputStreamEndsInNewline || !outputStreamContainsContent)
						includeInOutput = false;
				}
			}

			if (includeInOutput) {
				outputStream.Add (obj);
				OutputStreamDirty();
			}
		}

		void TrimNewlinesFromOutputStream()
		{
			int removeWhitespaceFrom = -1;

			// Work back from the end, and try to find the point where
			// we need to start removing content.
			//  - Simply work backwards to find the first newline in a string of whitespace
			// e.g. This is the content   \n   \n\n
			//                            ^---------^ whitespace to remove
			//                        ^--- first while loop stops here
			int i = outputStream.Count-1;
			while (i >= 0) {
				var obj = outputStream [i];
				var cmd = obj as ControlCommand;
				var txt = obj as StringValue;

				if (cmd || (txt && txt.isNonWhitespace)) {
					break;
				} 
				else if (txt && txt.isNewline) {
					removeWhitespaceFrom = i;
				}
				i--;
			}

			// Remove the whitespace
			if (removeWhitespaceFrom >= 0) {
				i=removeWhitespaceFrom;
				while(i < outputStream.Count) {
					var text = outputStream [i] as StringValue;
					if (text) {
						outputStream.RemoveAt (i);
					} else {
						i++;
					}
				}
			}

			OutputStreamDirty();
		}

		// Only called when non-whitespace is appended
		void RemoveExistingGlue()
		{
			for (int i = outputStream.Count - 1; i >= 0; i--) {
				var c = outputStream [i];
				if (c is Glue) {
					outputStream.RemoveAt (i);
				} else if( c is ControlCommand ) { // e.g. BeginString
					break;
				}
			}

			OutputStreamDirty();
		}

		public bool outputStreamEndsInNewline {
			get {
				if (outputStream.Count > 0) {

					for (int i = outputStream.Count - 1; i >= 0; i--) {
						var obj = outputStream [i];
						if (obj is ControlCommand) // e.g. BeginString
							break;
						var text = outputStream [i] as StringValue;
						if (text) {
							if (text.isNewline)
								return true;
							else if (text.isNonWhitespace)
								break;
						}
					}
				}

				return false;
			}
		}

		public bool outputStreamContainsContent {
			get {
				foreach (var content in outputStream) {
					if (content is StringValue)
						return true;
				}
				return false;
			}
		}

		public bool inStringEvaluation {
			get {
				for (int i = outputStream.Count - 1; i >= 0; i--) {
					var cmd = outputStream [i] as ControlCommand;
					if (cmd && cmd.commandType == ControlCommand.CommandType.BeginString) {
						return true;
					}
				}

				return false;
			}
		}

		public void PushEvaluationStack(Runtime.Object obj)
		{
			// Include metadata about the origin List for list values when
			// they're used, so that lower level functions can make use
			// of the origin list to get related items, or make comparisons
			// with the integer values etc.
			var listValue = obj as ListValue;
			if (listValue) {
				
				// Update origin when list is has something to indicate the list origin
				var rawList = listValue.value;
				if (rawList.originNames != null) {
					if( rawList.origins == null ) rawList.origins = new List<ListDefinition>();
					rawList.origins.Clear();

					foreach (var n in rawList.originNames) {
						ListDefinition def = null;
						story.listDefinitions.TryListGetDefinition (n, out def);
						if( !rawList.origins.Contains(def) )
							rawList.origins.Add (def);
					}
				}
			}

			evaluationStack.Add(obj);
		}

		public Runtime.Object PopEvaluationStack()
		{
			var obj = evaluationStack [evaluationStack.Count - 1];
			evaluationStack.RemoveAt (evaluationStack.Count - 1);
			return obj;
		}

		public Runtime.Object PeekEvaluationStack()
		{
			return evaluationStack [evaluationStack.Count - 1];
		}

		public List<Runtime.Object> PopEvaluationStack(int numberOfObjects)
		{
			if(numberOfObjects > evaluationStack.Count) {
				throw new System.Exception ("trying to pop too many objects");
			}

			var popped = evaluationStack.GetRange (evaluationStack.Count - numberOfObjects, numberOfObjects);
			evaluationStack.RemoveRange (evaluationStack.Count - numberOfObjects, numberOfObjects);
			return popped;
		}

		/// <summary>
		/// Ends the current ink flow, unwrapping the callstack but without
		/// affecting any variables. Useful if the ink is (say) in the middle
		/// a nested tunnel, and you want it to reset so that you can divert
		/// elsewhere using ChoosePathString(). Otherwise, after finishing
		/// the content you diverted to, it would continue where it left off.
		/// Calling this is equivalent to calling -> END in ink.
		/// </summary>
		public void ForceEnd()
		{
			callStack.Reset();

			_currentFlow.currentChoices.Clear();

			currentPointer = Pointer.Null;
			previousPointer = Pointer.Null;

			didSafeExit = true;
		}

		// Add the end of a function call, trim any whitespace from the end.
		// We always trim the start and end of the text that a function produces.
		// The start whitespace is discard as it is generated, and the end
		// whitespace is trimmed in one go here when we pop the function.
		void TrimWhitespaceFromFunctionEnd ()
		{
			Debug.Assert (callStack.currentElement.type == PushPopType.Function);

			var functionStartPoint = callStack.currentElement.functionStartInOuputStream;

			// If the start point has become -1, it means that some non-whitespace
			// text has been pushed, so it's safe to go as far back as we're able.
			if (functionStartPoint == -1) {
				functionStartPoint = 0;
			}

			// Trim whitespace from END of function call
			for (int i = outputStream.Count - 1; i >= functionStartPoint; i--) {
				var obj = outputStream [i];
				var txt = obj as StringValue;
				var cmd = obj as ControlCommand;
				if (!txt) continue;
				if (cmd) break;

				if (txt.isNewline || txt.isInlineWhitespace) {
					outputStream.RemoveAt (i);
					OutputStreamDirty ();
				} else {
					break;
				}
			}
		}

		public void PopCallstack (PushPopType? popType = null)
		{
			// Add the end of a function call, trim any whitespace from the end.
			if (callStack.currentElement.type == PushPopType.Function)
				TrimWhitespaceFromFunctionEnd ();

			callStack.Pop (popType);
		}

		// Don't make public since the method need to be wrapped in Story for visit counting
		public void SetChosenPath(Path path, bool incrementingTurnIndex)
		{
			// Changing direction, assume we need to clear current set of choices
			_currentFlow.currentChoices.Clear ();

			var newPointer = story.PointerAtPath (path);
			if (!newPointer.isNull && newPointer.index == -1)
				newPointer.index = 0;

			currentPointer = newPointer;

			if( incrementingTurnIndex )
				currentTurnIndex++;
		}

		public void StartFunctionEvaluationFromGame (Container funcContainer, params object[] arguments)
		{
			callStack.Push (PushPopType.FunctionEvaluationFromGame, evaluationStack.Count);
			callStack.currentElement.currentPointer = Pointer.StartOf (funcContainer);

			PassArgumentsToEvaluationStack (arguments);
		}

		public void PassArgumentsToEvaluationStack (params object [] arguments)
		{
			// Pass arguments onto the evaluation stack
			if (arguments != null) {
				for (int i = 0; i < arguments.Length; i++) {
					if (!(arguments [i] is int || arguments [i] is float || arguments [i] is string || arguments [i] is InkList)) {
						throw new System.ArgumentException ("ink arguments when calling EvaluateFunction / ChoosePathStringWithParameters must be int, float, string or InkList. Argument was "+(arguments [i] == null ? "null" : arguments [i].GetType().Name));
					}

					PushEvaluationStack (Runtime.Value.Create (arguments [i]));
				}
			}
		}
			
		public bool TryExitFunctionEvaluationFromGame ()
		{
			if( callStack.currentElement.type == PushPopType.FunctionEvaluationFromGame ) {
				currentPointer = Pointer.Null;
				didSafeExit = true;
				return true;
			}

			return false;
		}

		public object CompleteFunctionEvaluationFromGame ()
		{
			if (callStack.currentElement.type != PushPopType.FunctionEvaluationFromGame) {
				throw new Exception ("Expected external function evaluation to be complete. Stack trace: "+callStack.callStackTrace);
			}

			int originalEvaluationStackHeight = callStack.currentElement.evaluationStackHeightWhenPushed;
			
			// Do we have a returned value?
			// Potentially pop multiple values off the stack, in case we need
			// to clean up after ourselves (e.g. caller of EvaluateFunction may 
			// have passed too many arguments, and we currently have no way to check for that)
			Runtime.Object returnedObj = null;
			while (evaluationStack.Count > originalEvaluationStackHeight) {
				var poppedObj = PopEvaluationStack ();
				if (returnedObj == null)
					returnedObj = poppedObj;
			}

			// Finally, pop the external function evaluation
			PopCallstack (PushPopType.FunctionEvaluationFromGame);

			// What did we get back?
			if (returnedObj) {
				if (returnedObj is Runtime.Void)
					return null;

				// Some kind of value, if not void
				var returnVal = returnedObj as Runtime.Value;

				// DivertTargets get returned as the string of components
				// (rather than a Path, which isn't public)
				if (returnVal.valueType == ValueType.DivertTarget) {
					return returnVal.valueObject.ToString ();
				}

				// Other types can just have their exact object type:
				// int, float, string. VariablePointers get returned as strings.
				return returnVal.valueObject;
			}

			return null;
		}

		public void AddError(string message, bool isWarning)
		{
			if (!isWarning) {
				if (currentErrors == null) currentErrors = new List<string> ();
				currentErrors.Add (message);
			} else {
				if (currentWarnings == null) currentWarnings = new List<string> ();
				currentWarnings.Add (message);
			}
		}

		void OutputStreamDirty()
		{
			_outputStreamTextDirty = true;
			_outputStreamTagsDirty = true;
		}

		// REMEMBER! REMEMBER! REMEMBER!
		// When adding state, update the Copy method and serialisation
		// REMEMBER! REMEMBER! REMEMBER!


		Dictionary<string, int> _visitCounts;
		Dictionary<string, int> _turnIndices;
		bool _outputStreamTextDirty = true;
		bool _outputStreamTagsDirty = true;

		StatePatch _patch;

		Flow _currentFlow;
		Dictionary<string, Flow> _namedFlows;
		const string kDefaultFlowName = "DEFAULT_FLOW";
	}
	public class Profiler{
		/// <summary>
		/// The root node in the hierarchical tree of recorded ink timings.
		/// </summary>
		public ProfileNode rootNode {
			get {
				return _rootNode;
			}
		}

		public Profiler() {
			_rootNode = new ProfileNode();
		}

		/// <summary>
		/// Generate a printable report based on the data recording during profiling.
		/// </summary>
		public string Report() {
			var sb = new StringBuilder();
			sb.AppendFormat("{0} CONTINUES / LINES:\n", _numContinues);
			sb.AppendFormat("TOTAL TIME: {0}\n", FormatMillisecs(_continueTotal));
			sb.AppendFormat("SNAPSHOTTING: {0}\n", FormatMillisecs(_snapTotal));
			sb.AppendFormat("OTHER: {0}\n", FormatMillisecs(_continueTotal - (_stepTotal + _snapTotal)));
			sb.Append(_rootNode.ToString());
			return sb.ToString();
		}

		public void PreContinue() {
			_continueWatch.Reset();
			_continueWatch.Start();
		}

		public void PostContinue() {
			_continueWatch.Stop();
			_continueTotal += Millisecs(_continueWatch);
			_numContinues++;
		}

		public void PreStep() {
			_currStepStack = null;
			_stepWatch.Reset();
			_stepWatch.Start();
		}

		public void Step(CallStack callstack) 
		{
			_stepWatch.Stop();

			var stack = new string[callstack.elements.Count];
			for(int i=0; i<stack.Length; i++) {
				string stackElementName = "";
				if(!callstack.elements[i].currentPointer.isNull) {
					var objPath = callstack.elements[i].currentPointer.path;

					for(int c=0; c<objPath.length; c++) {
						var comp = objPath.GetComponent(c);
						if( !comp.isIndex ) {
							stackElementName = comp.name;
							break;
						}
					}

				}
				stack[i] = stackElementName;
			}
				
			_currStepStack = stack;

			var currObj = callstack.currentElement.currentPointer.Resolve();

			string stepType = null;
			var controlCommandStep = currObj as ControlCommand;
			if( controlCommandStep )
				stepType = controlCommandStep.commandType.ToString() + " CC";
			else
				stepType = currObj.GetType().Name;

			_currStepDetails = new StepDetails {
				type = stepType,
				obj = currObj
			};

			_stepWatch.Start();
		}

		public void PostStep() {
			_stepWatch.Stop();

			var duration = Millisecs(_stepWatch);
			_stepTotal += duration;

			_rootNode.AddSample(_currStepStack, duration);

			_currStepDetails.time = duration;
			_stepDetails.Add(_currStepDetails);
		}

		/// <summary>
		/// Generate a printable report specifying the average and maximum times spent
		/// stepping over different internal ink instruction types.
		/// This report type is primarily used to profile the ink engine itself rather
		/// than your own specific ink.
		/// </summary>
		public string StepLengthReport()
		{
			var sb = new StringBuilder();

			sb.AppendLine("TOTAL: "+_rootNode.totalMillisecs+"ms");

			var averageStepTimes = _stepDetails
				.GroupBy(s => s.type)
				.Select(typeToDetails => new KeyValuePair<string, double>(typeToDetails.Key, typeToDetails.Average(d => d.time)))
				.OrderByDescending(stepTypeToAverage => stepTypeToAverage.Value)
				.Select(stepTypeToAverage => {
					var typeName = stepTypeToAverage.Key;
					var time = stepTypeToAverage.Value;
					return typeName + ": " + time + "ms";
				})
				.ToArray();

			sb.AppendLine("AVERAGE STEP TIMES: "+string.Join(", ", averageStepTimes));

			var accumStepTimes = _stepDetails
				.GroupBy(s => s.type)
				.Select(typeToDetails => new KeyValuePair<string, double>(typeToDetails.Key + " (x"+typeToDetails.Count()+")", typeToDetails.Sum(d => d.time)))
				.OrderByDescending(stepTypeToAccum => stepTypeToAccum.Value)
				.Select(stepTypeToAccum => {
					var typeName = stepTypeToAccum.Key;
					var time = stepTypeToAccum.Value;
					return typeName + ": " + time;
				})
				.ToArray();

			sb.AppendLine("ACCUMULATED STEP TIMES: "+string.Join(", ", accumStepTimes));

			return sb.ToString();
		}

		/// <summary>
		/// Create a large log of all the internal instructions that were evaluated while profiling was active.
		/// Log is in a tab-separated format, for easy loading into a spreadsheet application.
		/// </summary>
		public string Megalog()
		{
			var sb = new StringBuilder();

			sb.AppendLine("Step type\tDescription\tPath\tTime");

			foreach(var step in _stepDetails) {
				sb.Append(step.type);
				sb.Append("\t");
				sb.Append(step.obj.ToString());
				sb.Append("\t");
				sb.Append(step.obj.path);
				sb.Append("\t");
				sb.AppendLine(step.time.ToString("F8"));
			}

			return sb.ToString();
		}

		public void PreSnapshot() {
			_snapWatch.Reset();
			_snapWatch.Start();
		}

		public void PostSnapshot() {
			_snapWatch.Stop();
			_snapTotal += Millisecs(_snapWatch);
		}

		double Millisecs(Stopwatch watch)
		{
			var ticks = watch.ElapsedTicks;
			return ticks * _millisecsPerTick;
		}

		public static string FormatMillisecs(double num) {
			if( num > 5000 ) {
				return string.Format("{0:N1} secs", num / 1000.0);
			} if( num > 1000 ) {
				return string.Format("{0:N2} secs", num / 1000.0);
			} else if( num > 100 ) {
				return string.Format("{0:N0} ms", num);
			} else if( num > 1 ) {
				return string.Format("{0:N1} ms", num);
			} else if( num > 0.01 ) {
				return string.Format("{0:N3} ms", num);
			} else {
				return string.Format("{0:N} ms", num);
			}
		}

		Stopwatch _continueWatch = new Stopwatch();
		Stopwatch _stepWatch = new Stopwatch();
		Stopwatch _snapWatch = new Stopwatch();

		double _continueTotal;
		double _snapTotal;
		double _stepTotal;

		string[] _currStepStack;
		StepDetails _currStepDetails;
		ProfileNode _rootNode;
		int _numContinues;

		struct StepDetails {
			public string type;
			public Runtime.Object obj;
			public double time;
		}
		List<StepDetails> _stepDetails = new List<StepDetails>();

		static double _millisecsPerTick = 1000.0 / Stopwatch.Frequency;
	}
	public class ProfileNode {

		/// <summary>
		/// The key for the node corresponds to the printable name of the callstack element.
		/// </summary>		
		public readonly string key;


		#pragma warning disable 0649
		/// <summary>
		/// Horribly hacky field only used by ink unity integration,
		/// but saves constructing an entire data structure that mirrors
		/// the one in here purely to store the state of whether each
		/// node in the UI has been opened or not  /// </summary>
		public bool openInUI;
		#pragma warning restore 0649

		/// <summary>
		/// Whether this node contains any sub-nodes - i.e. does it call anything else
		/// that has been recorded?
		/// </summary>
		/// <value><c>true</c> if has children; otherwise, <c>false</c>.</value>
		public bool hasChildren {
			get {
				return _nodes != null && _nodes.Count > 0;
			}
		}

		/// <summary>
		/// Total number of milliseconds this node has been active for.
		/// </summary>
		public int totalMillisecs {
			get {
				return (int)_totalMillisecs;
			}
		}

		public ProfileNode() {

		}

		public ProfileNode(string key) {
			this.key = key;
		}

		public void AddSample(string[] stack, double duration) {
			AddSample(stack, -1, duration);
		}

		void AddSample(string[] stack, int stackIdx, double duration) {

			_totalSampleCount++;
			_totalMillisecs += duration;

			if( stackIdx == stack.Length-1 ) {
				_selfSampleCount++;
				_selfMillisecs += duration;
			}

			if( stackIdx+1 < stack.Length )
				AddSampleToNode(stack, stackIdx+1, duration);
		}

		void AddSampleToNode(string[] stack, int stackIdx, double duration)
		{
			var nodeKey = stack[stackIdx];
			if( _nodes == null ) _nodes = new Dictionary<string, ProfileNode>();

			ProfileNode node;
			if( !_nodes.TryGetValue(nodeKey, out node) ) {
				node = new ProfileNode(nodeKey);
				_nodes[nodeKey] = node;
			}

			node.AddSample(stack, stackIdx, duration);
		}

		/// <summary>
		/// Returns a sorted enumerable of the nodes in descending order of
		/// how long they took to run.
		/// </summary>
		public IEnumerable<KeyValuePair<string, ProfileNode>> descendingOrderedNodes {
			get {
				if( _nodes == null ) return null;
				return _nodes.OrderByDescending(keyNode => keyNode.Value._totalMillisecs);
			}
		}

		void PrintHierarchy(StringBuilder sb, int indent)
		{
			Pad(sb, indent);

			sb.Append(key);
			sb.Append(": ");
			sb.AppendLine(ownReport);

			if( _nodes == null ) return;

			foreach(var keyNode in descendingOrderedNodes) {
				keyNode.Value.PrintHierarchy(sb, indent+1);
			}
		}

		/// <summary>
		/// Generates a string giving timing information for this single node, including
		/// total milliseconds spent on the piece of ink, the time spent within itself
		/// (v.s. spent in children), as well as the number of samples (instruction steps)
		/// recorded for both too.
		/// </summary>
		/// <value>The own report.</value>
		public string ownReport {
			get {
				var sb = new StringBuilder();
				sb.Append("total ");
				sb.Append(Profiler.FormatMillisecs(_totalMillisecs));
				sb.Append(", self ");
				sb.Append(Profiler.FormatMillisecs(_selfMillisecs));
				sb.Append(" (");
				sb.Append(_selfSampleCount);
				sb.Append(" self samples, ");
				sb.Append(_totalSampleCount);
				sb.Append(" total)");
				return sb.ToString();
			}
			
		}

		void Pad(StringBuilder sb, int spaces)
		{
			for(int i=0; i<spaces; i++) sb.Append("   ");
		}

		/// <summary>
		/// String is a report of the sub-tree from this node, but without any of the header information
		/// that's prepended by the Profiler in its Report() method.
		/// </summary>
		public override string ToString ()
		{
			var sb = new StringBuilder();
			PrintHierarchy(sb, 0);
			return sb.ToString();
		}

		Dictionary<string, ProfileNode> _nodes;
		double _selfMillisecs;
		double _totalMillisecs;
		int _selfSampleCount;
		int _totalSampleCount;
	}
	public interface INamedContent{
		string name { get; }
		bool hasValidName { get; }
	}
	public class Container : Object, INamedContent{
		public string name { get; set; }

		public List<Runtime.Object> content { 
			get {
				return _content;
			}
			set {
				AddContent (value);
			}
		}
		List<Runtime.Object> _content;

		public Dictionary<string, INamedContent> namedContent { get; set; }

		public Dictionary<string, Runtime.Object> namedOnlyContent { 
			get {
				var namedOnlyContentDict = new Dictionary<string, Runtime.Object>();
				foreach (var kvPair in namedContent) {
					namedOnlyContentDict [kvPair.Key] = (Runtime.Object)kvPair.Value;
				}

				foreach (var c in content) {
					var named = c as INamedContent;
					if (named != null && named.hasValidName) {
						namedOnlyContentDict.Remove (named.name);
					}
				}

				if (namedOnlyContentDict.Count == 0)
					namedOnlyContentDict = null;

				return namedOnlyContentDict;
			} 
			set {
				var existingNamedOnly = namedOnlyContent;
				if (existingNamedOnly != null) {
					foreach (var kvPair in existingNamedOnly) {
						namedContent.Remove (kvPair.Key);
					}
				}

				if (value == null)
					return;
				
				foreach (var kvPair in value) {
					var named = kvPair.Value as INamedContent;
					if( named != null )
						AddToNamedContentOnly (named);
				}
			}
		}
			
		public bool visitsShouldBeCounted { get; set; }
		public bool turnIndexShouldBeCounted { get; set; }
		public bool countingAtStartOnly { get; set; }

		[Flags]
		public enum CountFlags
		{
			Visits         = 1,
			Turns          = 2,
			CountStartOnly = 4
		}
				
		public int countFlags
		{
			get {
				CountFlags flags = 0;
				if (visitsShouldBeCounted)    flags |= CountFlags.Visits;
				if (turnIndexShouldBeCounted) flags |= CountFlags.Turns;
				if (countingAtStartOnly)      flags |= CountFlags.CountStartOnly;

				// If we're only storing CountStartOnly, it serves no purpose,
				// since it's dependent on the other two to be used at all.
				// (e.g. for setting the fact that *if* a gather or choice's
				// content is counted, then is should only be counter at the start)
				// So this is just an optimisation for storage.
				if (flags == CountFlags.CountStartOnly) {
					flags = 0;
				}

				return (int)flags;
			}
			set {
				var flag = (CountFlags)value;
				if ((flag & CountFlags.Visits) > 0) visitsShouldBeCounted = true;
				if ((flag & CountFlags.Turns) > 0)  turnIndexShouldBeCounted = true;
				if ((flag & CountFlags.CountStartOnly) > 0) countingAtStartOnly = true;
			}
		}

		public bool hasValidName 
		{
			get { return name != null && name.Length > 0; }
		}

		public Path pathToFirstLeafContent
		{
			get {
				if( _pathToFirstLeafContent == null )
					_pathToFirstLeafContent = path.PathByAppendingPath (internalPathToFirstLeafContent);

				return _pathToFirstLeafContent;
			}
		}
		Path _pathToFirstLeafContent;

		Path internalPathToFirstLeafContent
		{
			get {
				var components = new List<Path.Component>();
				var container = this;
				while (container != null) {
					if (container.content.Count > 0) {
						components.Add (new Path.Component (0));
						container = container.content [0] as Container;
					}
				}
				return new Path(components);
			}
		}

		public Container ()
		{
			_content = new List<Runtime.Object> ();
			namedContent = new Dictionary<string, INamedContent> ();
		}

		public void AddContent(Runtime.Object contentObj)
		{
			content.Add (contentObj);

			if (contentObj.parent) {
				throw new System.Exception ("content is already in " + contentObj.parent);
			}

			contentObj.parent = this;

			TryAddNamedContent (contentObj);
		}

		public void AddContent(IList<Runtime.Object> contentList)
		{
			foreach (var c in contentList) {
				AddContent (c);
			}
		}

		public void InsertContent(Runtime.Object contentObj, int index)
		{
			content.Insert (index, contentObj);

			if (contentObj.parent) {
				throw new System.Exception ("content is already in " + contentObj.parent);
			}

			contentObj.parent = this;

			TryAddNamedContent (contentObj);
		}
			
		public void TryAddNamedContent(Runtime.Object contentObj)
		{
			var namedContentObj = contentObj as INamedContent;
			if (namedContentObj != null && namedContentObj.hasValidName) {
				AddToNamedContentOnly (namedContentObj);
			}
		}

		public void AddToNamedContentOnly(INamedContent namedContentObj)
		{
			Debug.Assert (namedContentObj is Runtime.Object, "Can only add Runtime.Objects to a Runtime.Container");
			var runtimeObj = (Runtime.Object)namedContentObj;
			runtimeObj.parent = this;

			namedContent [namedContentObj.name] = namedContentObj;
		}

		public void AddContentsOfContainer(Container otherContainer)
		{
			content.AddRange (otherContainer.content);
			foreach (var obj in otherContainer.content) {
				obj.parent = this;
				TryAddNamedContent (obj);
			}
		}

		protected Runtime.Object ContentWithPathComponent(Path.Component component)
		{
			if (component.isIndex) {

				if (component.index >= 0 && component.index < content.Count) {
					return content [component.index];
				}

				// When path is out of range, quietly return nil
				// (useful as we step/increment forwards through content)
				else {
					return null;
				}

			} 

			else if (component.isParent) {
				return this.parent;
			}

			else {
				INamedContent foundContent = null;
				if (namedContent.TryGetValue (component.name, out foundContent)) {
					return (Runtime.Object)foundContent;
				} else {
					return null;
				}
			}
		}

		public SearchResult ContentAtPath(Path path, int partialPathStart = 0, int partialPathLength = -1)
		{
			if (partialPathLength == -1)
				partialPathLength = path.length;

			var result = new SearchResult ();
			result.approximate = false;

			Container currentContainer = this;
			Runtime.Object currentObj = this;

			for (int i = partialPathStart; i < partialPathLength; ++i) {
				var comp = path.GetComponent(i);

				// Path component was wrong type
				if (currentContainer == null) {
					result.approximate = true;
					break;
				}

				var foundObj = currentContainer.ContentWithPathComponent(comp);

				// Couldn't resolve entire path?
				if (foundObj == null) {
					result.approximate = true;
					break;
				} 

				currentObj = foundObj;
				currentContainer = foundObj as Container;
			}

			result.obj = currentObj;

			return result;
		}
		 
		public void BuildStringOfHierarchy(StringBuilder sb, int indentation, Runtime.Object pointedObj)
		{
			Action appendIndentation = () => { 
				const int spacesPerIndent = 4;
				for(int i=0; i<spacesPerIndent*indentation;++i) { 
					sb.Append(" "); 
				} 
			};

			appendIndentation ();
			sb.Append("[");

			if (this.hasValidName) {
				sb.AppendFormat (" ({0})", this.name);
			}

			if (this == pointedObj) {
				sb.Append ("  <---");
			}

			sb.AppendLine ();

			indentation++;
			
			for (int i=0; i<content.Count; ++i) {

				var obj = content [i];

				if (obj is Container) {

					var container = (Container)obj;

					container.BuildStringOfHierarchy (sb, indentation, pointedObj);

				} else {
					appendIndentation ();
					if (obj is StringValue) {
						sb.Append ("\"");
						sb.Append (obj.ToString ().Replace ("\n", "\\n"));
						sb.Append ("\"");
					} else {
						sb.Append (obj.ToString ());
					}
				}

				if (i != content.Count - 1) {
					sb.Append (",");
				}

				if ( !(obj is Container) && obj == pointedObj ) {
					sb.Append ("  <---");
				}
					
				sb.AppendLine ();
			}
				

			var onlyNamed = new Dictionary<string, INamedContent> ();

			foreach (var objKV in namedContent) {
				if (content.Contains ((Runtime.Object)objKV.Value)) {
					continue;
				} else {
					onlyNamed.Add (objKV.Key, objKV.Value);
				}
			}

			if (onlyNamed.Count > 0) {
				appendIndentation ();
				sb.AppendLine ("-- named: --");

				foreach (var objKV in onlyNamed) {

					Debug.Assert (objKV.Value is Container, "Can only print out named Containers");
					var container = (Container)objKV.Value;
					container.BuildStringOfHierarchy (sb, indentation, pointedObj);

					sb.AppendLine ();

				}
			}


			indentation--;

			appendIndentation ();
			sb.Append ("]");
		}

		public virtual string BuildStringOfHierarchy()
		{
			var sb = new StringBuilder ();

			BuildStringOfHierarchy (sb, 0, null);

			return sb.ToString ();
		}
	}
	public class ChoicePoint : Object {
		public Path pathOnChoice {
			get {
				// Resolve any relative paths to global ones as we come across them
				if (_pathOnChoice != null && _pathOnChoice.isRelative) {
					var choiceTargetObj = choiceTarget;
					if (choiceTargetObj) {
						_pathOnChoice = choiceTargetObj.path;
					}
				}
				return _pathOnChoice;
			}
			set {
				_pathOnChoice = value;
			}
		}
		Path _pathOnChoice;

		public Container choiceTarget {
			get {
				return this.ResolvePath (_pathOnChoice).container;
			}
		}

		public string pathStringOnChoice {
			get {
				return CompactPathString (pathOnChoice);
			}
			set {
				pathOnChoice = new Path (value);
			}
		}

		public bool hasCondition { get; set; }
		public bool hasStartContent { get; set; }
		public bool hasChoiceOnlyContent { get; set; }
		public bool onceOnly { get; set; }
		public bool isInvisibleDefault { get; set; }

		public int flags {
			get {
				int flags = 0;
				if (hasCondition)         flags |= 1;
				if (hasStartContent)      flags |= 2;
				if (hasChoiceOnlyContent) flags |= 4;
				if (isInvisibleDefault)   flags |= 8;
				if (onceOnly)             flags |= 16;
				return flags;
			}
			set {
				hasCondition = (value & 1) > 0;
				hasStartContent = (value & 2) > 0;
				hasChoiceOnlyContent = (value & 4) > 0;
				isInvisibleDefault = (value & 8) > 0;
				onceOnly = (value & 16) > 0;
			}
		}

		public ChoicePoint (bool onceOnly)
		{
			this.onceOnly = onceOnly;
		}

		public ChoicePoint() : this(true) {}

		public override string ToString ()
		{
			int? targetLineNum = DebugLineNumberOfPath (pathOnChoice);
			string targetString = pathOnChoice.ToString ();

			if (targetLineNum != null) {
				targetString = " line " + targetLineNum + "("+targetString+")";
			} 

			return "Choice: -> " + targetString;
		}
	}
	public class Story : Object{
		/// <summary>
		/// The current version of the ink story file format.
		/// </summary>
		public const int inkVersionCurrent = 20;

		// Version numbers are for engine itself and story file, rather
		// than the story state save format
		//  -- old engine, new format: always fail
		//  -- new engine, old format: possibly cope, based on this number
		// When incrementing the version number above, the question you
		// should ask yourself is:
		//  -- Will the engine be able to load an old story file from 
		//     before I made these changes to the engine?
		//     If possible, you should support it, though it's not as
		//     critical as loading old save games, since it's an
		//     in-development problem only.

		/// <summary>
		/// The minimum legacy version of ink that can be loaded by the current version of the code.
		/// </summary>
		const int inkVersionMinimumCompatible = 18;

		/// <summary>
		/// The list of Choice objects available at the current point in
		/// the Story. This list will be populated as the Story is stepped
		/// through with the Continue() method. Once canContinue becomes
		/// false, this list will be populated, and is usually
		/// (but not always) on the final Continue() step.
		/// </summary>
		public List<Choice> currentChoices
		{
			get 
			{
				// Don't include invisible choices for external usage.
				var choices = new List<Choice>();
				foreach (var c in _state.currentChoices) {
					if (!c.isInvisibleDefault) {
						c.index = choices.Count;
						choices.Add (c);
					}
				}
				return choices;
			}
		}
			
		/// <summary>
		/// The latest line of text to be generated from a Continue() call.
		/// </summary>
		public string currentText { 
			get  { 
				IfAsyncWeCant ("call currentText since it's a work in progress");
				return state.currentText; 
			} 
		}

		/// <summary>
		/// Gets a list of tags as defined with '#' in source that were seen
		/// during the latest Continue() call.
		/// </summary>
		public List<string> currentTags { 
			get { 
				IfAsyncWeCant ("call currentTags since it's a work in progress");
				return state.currentTags; 
			} 
		}

		/// <summary>
		/// Any errors generated during evaluation of the Story.
		/// </summary>
		public List<string> currentErrors { get { return state.currentErrors; } }

		/// <summary>
		/// Any warnings generated during evaluation of the Story.
		/// </summary>
		public List<string> currentWarnings { get { return state.currentWarnings; } }

		/// <summary>
		/// The current flow name if using multi-flow funtionality - see SwitchFlow
		/// </summary>
		public string currentFlowName => state.currentFlowName;

		/// <summary>
		/// Whether the currentErrors list contains any errors.
		/// THIS MAY BE REMOVED - you should be setting an error handler directly
		/// using Story.onError.
		/// </summary>
		public bool hasError { get { return state.hasError; } }

		/// <summary>
		/// Whether the currentWarnings list contains any warnings.
		/// </summary>
		public bool hasWarning { get { return state.hasWarning; } }

		/// <summary>
		/// The VariablesState object contains all the global variables in the story.
		/// However, note that there's more to the state of a Story than just the
		/// global variables. This is a convenience accessor to the full state object.
		/// </summary>
		public VariablesState variablesState{ get { return state.variablesState; } }

		public ListDefinitionsOrigin listDefinitions {
			get {
				return _listDefinitions;
			}
		}

		/// <summary>
		/// The entire current state of the story including (but not limited to):
		/// 
		///  * Global variables
		///  * Temporary variables
		///  * Read/visit and turn counts
		///  * The callstack and evaluation stacks
		///  * The current threads
		/// 
		/// </summary>
		public StoryState state { get { return _state; } }
		
		/// <summary>
		/// Error handler for all runtime errors in ink - i.e. problems
		/// with the source ink itself that are only discovered when playing
		/// the story.
		/// It's strongly recommended that you assign an error handler to your
		/// story instance to avoid getting exceptions for ink errors.
		/// </summary>
		public event ErrorHandler onError;
		
		/// <summary>
		/// Callback for when ContinueInternal is complete
		/// </summary>
		public event Action onDidContinue;
		/// <summary>
		/// Callback for when a choice is about to be executed
		/// </summary>
		public event Action<Choice> onMakeChoice;
		/// <summary>
		/// Callback for when a function is about to be evaluated
		/// </summary>
		public event Action<string, object[]> onEvaluateFunction;
		/// <summary>
		/// Callback for when a function has been evaluated
		/// This is necessary because evaluating a function can cause continuing
		/// </summary>
		public event Action<string, object[], string, object> onCompleteEvaluateFunction;
		/// <summary>
		/// Callback for when a path string is chosen
		/// </summary>
		public event Action<string, object[]> onChoosePathString;

		/// <summary>
		/// Start recording ink profiling information during calls to Continue on Story.
		/// Return a Profiler instance that you can request a report from when you're finished.
		/// </summary>
		public Profiler StartProfiling() {
			IfAsyncWeCant ("start profiling");
			_profiler = new Profiler();
			return _profiler;
		}

		/// <summary>
		/// Stop recording ink profiling information during calls to Continue on Story.
		/// To generate a report from the profiler, call 
		/// </summary>
		public void EndProfiling() {
			_profiler = null;
		}
			
		// Warning: When creating a Story using this constructor, you need to
		// call ResetState on it before use. Intended for compiler use only.
		// For normal use, use the constructor that takes a json string.
		public Story (Container contentContainer, List<Runtime.ListDefinition> lists = null)
		{
			_mainContentContainer = contentContainer;

			if (lists != null)
				_listDefinitions = new ListDefinitionsOrigin (lists);

			_externals = new Dictionary<string, ExternalFunctionDef> ();
		}

		/// <summary>
		/// Construct a Story object using a JSON string compiled through inklecate.
		/// </summary>
		public Story(string jsonString) : this((Container)null)
		{
			Dictionary<string, object> rootObject = SimpleJson.TextToDictionary (jsonString);

			object versionObj = rootObject ["inkVersion"];
			if (versionObj == null)
				throw new System.Exception ("ink version number not found. Are you sure it's a valid .ink.json file?");

			int formatFromFile = (int)versionObj;
			if (formatFromFile > inkVersionCurrent) {
				throw new System.Exception ("Version of ink used to build story was newer than the current version of the engine");
			} else if (formatFromFile < inkVersionMinimumCompatible) {
				throw new System.Exception ("Version of ink used to build story is too old to be loaded by this version of the engine");
			} else if (formatFromFile != inkVersionCurrent) {
				System.Diagnostics.Debug.WriteLine ("WARNING: Version of ink used to build story doesn't match current version of engine. Non-critical, but recommend synchronising.");
			}
				
			var rootToken = rootObject ["root"];
			if (rootToken == null)
				throw new System.Exception ("Root node for ink not found. Are you sure it's a valid .ink.json file?");

			object listDefsObj;
			if (rootObject.TryGetValue ("listDefs", out listDefsObj)) {
				_listDefinitions = Json.JTokenToListDefinitions (listDefsObj);
			}

			_mainContentContainer = Json.JTokenToRuntimeObject (rootToken) as Container;

			ResetState ();
		}

		/// <summary>
		/// The Story itself in JSON representation.
		/// </summary>
		public string ToJson()
		{
			//return ToJsonOld();
			var writer = new SimpleJson.Writer();
			ToJson(writer);
			return writer.ToString();
		}

		/// <summary>
		/// The Story itself in JSON representation.
		/// </summary>
		public void ToJson(Stream stream)
		{
			var writer = new SimpleJson.Writer(stream);
			ToJson(writer);
		}

		void ToJson(SimpleJson.Writer writer)
		{
			writer.WriteObjectStart();

			writer.WriteProperty("inkVersion", inkVersionCurrent);

			// Main container content
			writer.WriteProperty("root", w => Json.WriteRuntimeContainer(w, _mainContentContainer));

			// List definitions
			if (_listDefinitions != null) {

				writer.WritePropertyStart("listDefs");
				writer.WriteObjectStart();

				foreach (ListDefinition def in _listDefinitions.lists)
				{
					writer.WritePropertyStart(def.name);
					writer.WriteObjectStart();

					foreach (var itemToVal in def.items)
					{
						InkListItem item = itemToVal.Key;
						int val = itemToVal.Value;
						writer.WriteProperty(item.itemName, val);
					}

					writer.WriteObjectEnd();
					writer.WritePropertyEnd();
				}

				writer.WriteObjectEnd();
				writer.WritePropertyEnd();
			}

			writer.WriteObjectEnd();
		}
			
		/// <summary>
		/// Reset the Story back to its initial state as it was when it was
		/// first constructed.
		/// </summary>
		public void ResetState()
		{
			// TODO: Could make this possible
			IfAsyncWeCant ("ResetState");

			_state = new StoryState (this);
			_state.variablesState.variableChangedEvent += VariableStateDidChangeEvent;

			ResetGlobals ();
		}

		void ResetErrors()
		{
			_state.ResetErrors ();
		}

		/// <summary>
		/// Unwinds the callstack. Useful to reset the Story's evaluation
		/// without actually changing any meaningful state, for example if
		/// you want to exit a section of story prematurely and tell it to
		/// go elsewhere with a call to ChoosePathString(...).
		/// Doing so without calling ResetCallstack() could cause unexpected
		/// issues if, for example, the Story was in a tunnel already.
		/// </summary>
		public void ResetCallstack()
		{
			IfAsyncWeCant ("ResetCallstack");

			_state.ForceEnd ();
		}

		void ResetGlobals()
		{
			if (_mainContentContainer.namedContent.ContainsKey ("global decl")) {
				var originalPointer = state.currentPointer;

				ChoosePath (new Path ("global decl"), incrementingTurnIndex: false);

				// Continue, but without validating external bindings,
				// since we may be doing this reset at initialisation time.
				ContinueInternal ();

				state.currentPointer = originalPointer;
			}

			state.variablesState.SnapshotDefaultGlobals ();
		}

		public void SwitchFlow(string flowName)
		{
			IfAsyncWeCant("switch flow");
			if (_asyncSaving) throw new System.Exception("Story is already in background saving mode, can't switch flow to "+flowName);

			state.SwitchFlow_Internal(flowName);
		}

		public void RemoveFlow(string flowName)
		{
			state.RemoveFlow_Internal(flowName);
		}

		public void SwitchToDefaultFlow()
		{
			state.SwitchToDefaultFlow_Internal();
		}


		/// <summary>
		/// Continue the story for one line of content, if possible.
		/// If you're not sure if there's more content available, for example if you
		/// want to check whether you're at a choice point or at the end of the story,
		/// you should call <c>canContinue</c> before calling this function.
		/// </summary>
		/// <returns>The line of text content.</returns>
		public string Continue()
		{
			ContinueAsync(0);
			return currentText;
		}


		/// <summary>
		/// Check whether more content is available if you were to call <c>Continue()</c> - i.e.
		/// are we mid story rather than at a choice point or at the end.
		/// </summary>
		/// <value><c>true</c> if it's possible to call <c>Continue()</c>.</value>
		public bool canContinue {
			get {
				return state.canContinue;
			}
		}

		/// <summary>
		/// If ContinueAsync was called (with milliseconds limit > 0) then this property
		/// will return false if the ink evaluation isn't yet finished, and you need to call 
		/// it again in order for the Continue to fully complete.
		/// </summary>
		public bool asyncContinueComplete {
			get {
				return !_asyncContinueActive;
			}
		}

		/// <summary>
		/// An "asnychronous" version of Continue that only partially evaluates the ink,
		/// with a budget of a certain time limit. It will exit ink evaluation early if
		/// the evaluation isn't complete within the time limit, with the
		/// asyncContinueComplete property being false.
		/// This is useful if ink evaluation takes a long time, and you want to distribute
		/// it over multiple game frames for smoother animation.
		/// If you pass a limit of zero, then it will fully evaluate the ink in the same
		/// way as calling Continue (and in fact, this exactly what Continue does internally).
		/// </summary>
		public void ContinueAsync (float millisecsLimitAsync)
		{
			if( !_hasValidatedExternals )
				ValidateExternalBindings ();

			ContinueInternal (millisecsLimitAsync);
		}

		void ContinueInternal (float millisecsLimitAsync = 0)
		{
			if( _profiler != null )
				_profiler.PreContinue();
			
			var isAsyncTimeLimited = millisecsLimitAsync > 0;

			_recursiveContinueCount++;

			// Doing either:
			//  - full run through non-async (so not active and don't want to be)
			//  - Starting async run-through
			if (!_asyncContinueActive) {
				_asyncContinueActive = isAsyncTimeLimited;
				
				if (!canContinue) {
					throw new Exception ("Can't continue - should check canContinue before calling Continue");
				}

				_state.didSafeExit = false;
				_state.ResetOutput ();

				// It's possible for ink to call game to call ink to call game etc
				// In this case, we only want to batch observe variable changes
				// for the outermost call.
				if (_recursiveContinueCount == 1)
					_state.variablesState.batchObservingVariableChanges = true;
			}

			// Start timing
			var durationStopwatch = new Stopwatch ();
			durationStopwatch.Start ();

			bool outputStreamEndsInNewline = false;
			_sawLookaheadUnsafeFunctionAfterNewline = false;
			do {

				try {
					outputStreamEndsInNewline = ContinueSingleStep ();
				} catch(StoryException e) {
					AddError (e.Message, useEndLineNumber:e.useEndLineNumber);
					break;
				}
				
				if (outputStreamEndsInNewline) 
					break;

				// Run out of async time?
				if (_asyncContinueActive && durationStopwatch.ElapsedMilliseconds > millisecsLimitAsync) {
					break;
				}

			} while(canContinue);

			durationStopwatch.Stop ();

			// 4 outcomes:
			//  - got newline (so finished this line of text)
			//  - can't continue (e.g. choices or ending)
			//  - ran out of time during evaluation
			//  - error
			//
			// Successfully finished evaluation in time (or in error)
			if (outputStreamEndsInNewline || !canContinue) {

				// Need to rewind, due to evaluating further than we should?
				if( _stateSnapshotAtLastNewline != null ) {
					RestoreStateSnapshot ();
				}

				// Finished a section of content / reached a choice point?
				if( !canContinue ) {
					if (state.callStack.canPopThread)
						AddError ("Thread available to pop, threads should always be flat by the end of evaluation?");

					if (state.generatedChoices.Count == 0 && !state.didSafeExit && _temporaryEvaluationContainer == null) {
						if (state.callStack.CanPop (PushPopType.Tunnel))
							AddError ("unexpectedly reached end of content. Do you need a '->->' to return from a tunnel?");
						else if (state.callStack.CanPop (PushPopType.Function))
							AddError ("unexpectedly reached end of content. Do you need a '~ return'?");
						else if (!state.callStack.canPop)
							AddError ("ran out of content. Do you need a '-> DONE' or '-> END'?");
						else
							AddError ("unexpectedly reached end of content for unknown reason. Please debug compiler!");
					}
				}

				state.didSafeExit = false;
				_sawLookaheadUnsafeFunctionAfterNewline = false;

				if (_recursiveContinueCount == 1)
					_state.variablesState.batchObservingVariableChanges = false;

				_asyncContinueActive = false;
				if(onDidContinue != null) onDidContinue();
			}

			_recursiveContinueCount--;

			if( _profiler != null )
				_profiler.PostContinue();

			// Report any errors that occured during evaluation.
			// This may either have been StoryExceptions that were thrown
			// and caught during evaluation, or directly added with AddError.
			if( state.hasError || state.hasWarning ) {
				if( onError != null ) {
					if( state.hasError ) {
						foreach(var err in state.currentErrors) {
							onError(err, ErrorType.Error);
						}
					}
					if( state.hasWarning ) {
						foreach(var err in state.currentWarnings) {
							onError(err, ErrorType.Warning);
						}
					}
					ResetErrors();
				} 
				
				// Throw an exception since there's no error handler
				else {
					var sb = new StringBuilder();
					sb.Append("Ink had ");
					if( state.hasError ) {
						sb.Append(state.currentErrors.Count);
						sb.Append(state.currentErrors.Count == 1 ? " error" : " errors");
						if( state.hasWarning ) sb.Append(" and ");
					}
					if( state.hasWarning ) {
						sb.Append(state.currentWarnings.Count);
						sb.Append(state.currentWarnings.Count == 1 ? " warning" : " warnings");
					}
					sb.Append(". It is strongly suggested that you assign an error handler to story.onError. The first issue was: ");
					sb.Append(state.hasError ? state.currentErrors[0] : state.currentWarnings[0]);

					// If you get this exception, please assign an error handler to your story.
					// If you're using Unity, you can do something like this when you create
					// your story:
					//
					// var story = new Ink.Runtime.Story(jsonTxt);
					// story.onError = (errorMessage, errorType) => {
					//     if( errorType == ErrorType.Warning )
					//         Debug.LogWarning(errorMessage);
					//     else
					//         Debug.LogError(errorMessage);
					// };
					//
					// 
					throw new StoryException(sb.ToString());
				}
			}
		}

		bool ContinueSingleStep ()
		{
			if (_profiler != null)
				_profiler.PreStep ();

			// Run main step function (walks through content)
			Step ();

			if (_profiler != null)
				_profiler.PostStep ();

			// Run out of content and we have a default invisible choice that we can follow?
			if (!canContinue && !state.callStack.elementIsEvaluateFromGame) {
				TryFollowDefaultInvisibleChoice ();
			}

			if (_profiler != null)
				_profiler.PreSnapshot ();

			// Don't save/rewind during string evaluation, which is e.g. used for choices
			if (!state.inStringEvaluation) {

				// We previously found a newline, but were we just double checking that
				// it wouldn't immediately be removed by glue?
				if (_stateSnapshotAtLastNewline != null) {

					// Has proper text or a tag been added? Then we know that the newline
					// that was previously added is definitely the end of the line.
					var change = CalculateNewlineOutputStateChange (
						_stateSnapshotAtLastNewline.currentText,       state.currentText, 
						_stateSnapshotAtLastNewline.currentTags.Count, state.currentTags.Count
					);

					// The last time we saw a newline, it was definitely the end of the line, so we
					// want to rewind to that point.
					if (change == OutputStateChange.ExtendedBeyondNewline || _sawLookaheadUnsafeFunctionAfterNewline) {
						RestoreStateSnapshot ();

						// Hit a newline for sure, we're done
						return true;
					} 

					// Newline that previously existed is no longer valid - e.g.
					// glue was encounted that caused it to be removed.
					else if (change == OutputStateChange.NewlineRemoved) {
						DiscardSnapshot();
					}
				}

				// Current content ends in a newline - approaching end of our evaluation
				if (state.outputStreamEndsInNewline) {

					// If we can continue evaluation for a bit:
					// Create a snapshot in case we need to rewind.
					// We're going to continue stepping in case we see glue or some
					// non-text content such as choices.
					if (canContinue) {

						// Don't bother to record the state beyond the current newline.
						// e.g.:
						// Hello world\n            // record state at the end of here
						// ~ complexCalculation()   // don't actually need this unless it generates text
						if (_stateSnapshotAtLastNewline == null)
							StateSnapshot ();
					}

					// Can't continue, so we're about to exit - make sure we
					// don't have an old state hanging around.
					else {
						DiscardSnapshot();
					}

				}

			}

			if (_profiler != null)
				_profiler.PostSnapshot ();

			// outputStreamEndsInNewline = false
			return false;
		}




		// Assumption: prevText is the snapshot where we saw a newline, and we're checking whether we're really done
		//             with that line. Therefore prevText will definitely end in a newline.
		//
		// We take tags into account too, so that a tag following a content line:
		//   Content
		//   # tag
		// ... doesn't cause the tag to be wrongly associated with the content above.
		enum OutputStateChange
		{
			NoChange,
			ExtendedBeyondNewline,
			NewlineRemoved
		}
		OutputStateChange CalculateNewlineOutputStateChange (string prevText, string currText, int prevTagCount, int currTagCount)
		{
			// Simple case: nothing's changed, and we still have a newline
			// at the end of the current content
			var newlineStillExists = currText.Length >= prevText.Length && currText [prevText.Length - 1] == '\n';
			if (prevTagCount == currTagCount && prevText.Length == currText.Length 
				&& newlineStillExists)
				return OutputStateChange.NoChange;

			// Old newline has been removed, it wasn't the end of the line after all
			if (!newlineStillExists) {
				return OutputStateChange.NewlineRemoved;
			}

			// Tag added - definitely the start of a new line
			if (currTagCount > prevTagCount)
				return OutputStateChange.ExtendedBeyondNewline;

			// There must be new content - check whether it's just whitespace
			for (int i = prevText.Length; i < currText.Length; i++) {
				var c = currText [i];
				if (c != ' ' && c != '\t') {
					return OutputStateChange.ExtendedBeyondNewline;
				}
			}

			// There's new text but it's just spaces and tabs, so there's still the potential
			// for glue to kill the newline.
			return OutputStateChange.NoChange;
		}


		/// <summary>
		/// Continue the story until the next choice point or until it runs out of content.
		/// This is as opposed to the Continue() method which only evaluates one line of
		/// output at a time.
		/// </summary>
		/// <returns>The resulting text evaluated by the ink engine, concatenated together.</returns>
		public string ContinueMaximally()
		{
			IfAsyncWeCant ("ContinueMaximally");

			var sb = new StringBuilder ();

			while (canContinue) {
				sb.Append (Continue ());
			}

			return sb.ToString ();
		}

		public SearchResult ContentAtPath(Path path)
		{
			return mainContentContainer.ContentAtPath (path);
		}

		public Runtime.Container KnotContainerWithName (string name)
		{
			INamedContent namedContainer;
			if (mainContentContainer.namedContent.TryGetValue (name, out namedContainer))
				return namedContainer as Container;
			else
				return null;
		}

		public Pointer PointerAtPath (Path path)
		{
			if (path.length == 0)
				return Pointer.Null;

			var p = new Pointer ();

			int pathLengthToUse = path.length;

			SearchResult result;
			if( path.lastComponent.isIndex ) {
				pathLengthToUse = path.length - 1;
				result = mainContentContainer.ContentAtPath (path, partialPathLength:pathLengthToUse);
				p.container = result.container;
				p.index = path.lastComponent.index;
			} else {
				result = mainContentContainer.ContentAtPath (path);
				p.container = result.container;
				p.index = -1;
			}

			if (result.obj == null || result.obj == mainContentContainer && pathLengthToUse > 0)
				Error ("Failed to find content at path '" + path + "', and no approximation of it was possible.");
			else if (result.approximate)
				Warning ("Failed to find content at path '" + path + "', so it was approximated to: '"+result.obj.path+"'.");

			return p;
		}

		// Maximum snapshot stack:
		//  - stateSnapshotDuringSave -- not retained, but returned to game code
		//  - _stateSnapshotAtLastNewline (has older patch)
		//  - _state (current, being patched)

		void StateSnapshot()
		{
			_stateSnapshotAtLastNewline = _state;
			_state = _state.CopyAndStartPatching();
		}

		void RestoreStateSnapshot()
		{
			// Patched state had temporarily hijacked our
			// VariablesState and set its own callstack on it,
			// so we need to restore that.
			// If we're in the middle of saving, we may also
			// need to give the VariablesState the old patch.
			_stateSnapshotAtLastNewline.RestoreAfterPatch();

			_state = _stateSnapshotAtLastNewline;
			_stateSnapshotAtLastNewline = null;

			// If save completed while the above snapshot was
			// active, we need to apply any changes made since
			// the save was started but before the snapshot was made.
			if( !_asyncSaving ) {
				_state.ApplyAnyPatch();
			}
		}

		void DiscardSnapshot()
		{
			// Normally we want to integrate the patch
			// into the main global/counts dictionaries.
			// However, if we're in the middle of async
			// saving, we simply stay in a "patching" state,
			// albeit with the newer cloned patch.
			if( !_asyncSaving )
				_state.ApplyAnyPatch();

			// No longer need the snapshot.
			_stateSnapshotAtLastNewline = null;
		}

		/// <summary>
		/// Advanced usage!
		/// If you have a large story, and saving state to JSON takes too long for your
		/// framerate, you can temporarily freeze a copy of the state for saving on 
		/// a separate thread. Internally, the engine maintains a "diff patch".
		/// When you've finished saving your state, call BackgroundSaveComplete()
		/// and that diff patch will be applied, allowing the story to continue
		/// in its usual mode.
		/// </summary>
		/// <returns>The state for background thread save.</returns>
		public StoryState CopyStateForBackgroundThreadSave()
		{
			IfAsyncWeCant("start saving on a background thread");
			if (_asyncSaving) throw new System.Exception("Story is already in background saving mode, can't call CopyStateForBackgroundThreadSave again!");
			var stateToSave = _state;
			_state = _state.CopyAndStartPatching();
			_asyncSaving = true;
			return stateToSave;
		}

		/// <summary>
		/// See CopyStateForBackgroundThreadSave. This method releases the
		/// "frozen" save state, applying its patch that it was using internally.
		/// </summary>
		public void BackgroundSaveComplete()
		{
			// CopyStateForBackgroundThreadSave must be called outside
			// of any async ink evaluation, since otherwise you'd be saving
			// during an intermediate state.
			// However, it's possible to *complete* the save in the middle of
			// a glue-lookahead when there's a state stored in _stateSnapshotAtLastNewline.
			// This state will have its own patch that is newer than the save patch.
			// We hold off on the final apply until the glue-lookahead is finished.
			// In that case, the apply is always done, it's just that it may
			// apply the looked-ahead changes OR it may simply apply the changes
			// made during the save process to the old _stateSnapshotAtLastNewline state.
			if ( _stateSnapshotAtLastNewline == null ) {
				_state.ApplyAnyPatch();
			}

			_asyncSaving = false;
		}



		void Step ()
		{
			bool shouldAddToStream = true;

			// Get current content
			var pointer = state.currentPointer;
			if (pointer.isNull) {
				return;
			}

			// Step directly to the first element of content in a container (if necessary)
			Container containerToEnter = pointer.Resolve () as Container;
			while(containerToEnter) {

				// Mark container as being entered
				VisitContainer (containerToEnter, atStart:true);

				// No content? the most we can do is step past it
				if (containerToEnter.content.Count == 0)
					break;


				pointer = Pointer.StartOf (containerToEnter);
				containerToEnter = pointer.Resolve() as Container;
			}
			state.currentPointer = pointer;

			if( _profiler != null ) {
				_profiler.Step(state.callStack);
			}

			// Is the current content object:
			//  - Normal content
			//  - Or a logic/flow statement - if so, do it
			// Stop flow if we hit a stack pop when we're unable to pop (e.g. return/done statement in knot
			// that was diverted to rather than called as a function)
			var currentContentObj = pointer.Resolve ();
			bool isLogicOrFlowControl = PerformLogicAndFlowControl (currentContentObj);

			// Has flow been forced to end by flow control above?
			if (state.currentPointer.isNull) {
				return;
			}

			if (isLogicOrFlowControl) {
				shouldAddToStream = false;
			}

			// Choice with condition?
			var choicePoint = currentContentObj as ChoicePoint;
			if (choicePoint) {
				var choice = ProcessChoice (choicePoint);
				if (choice) {
					state.generatedChoices.Add (choice);
				}

				currentContentObj = null;
				shouldAddToStream = false;
			}

			// If the container has no content, then it will be
			// the "content" itself, but we skip over it.
			if (currentContentObj is Container) {
				shouldAddToStream = false;
			}

			// Content to add to evaluation stack or the output stream
			if (shouldAddToStream) {

				// If we're pushing a variable pointer onto the evaluation stack, ensure that it's specific
				// to our current (possibly temporary) context index. And make a copy of the pointer
				// so that we're not editing the original runtime object.
				var varPointer = currentContentObj as VariablePointerValue;
				if (varPointer && varPointer.contextIndex == -1) {

					// Create new object so we're not overwriting the story's own data
					var contextIdx = state.callStack.ContextForVariableNamed(varPointer.variableName);
					currentContentObj = new VariablePointerValue (varPointer.variableName, contextIdx);
				}

				// Expression evaluation content
				if (state.inExpressionEvaluation) {
					state.PushEvaluationStack (currentContentObj);
				}
				// Output stream content (i.e. not expression evaluation)
				else {
					state.PushToOutputStream (currentContentObj);
				}
			}

			// Increment the content pointer, following diverts if necessary
			NextContent ();

			// Starting a thread should be done after the increment to the content pointer,
			// so that when returning from the thread, it returns to the content after this instruction.
			var controlCmd = currentContentObj as ControlCommand;
			if (controlCmd && controlCmd.commandType == ControlCommand.CommandType.StartThread) {
				state.callStack.PushThread ();
			}
		}

		// Mark a container as having been visited
		void VisitContainer(Container container, bool atStart)
		{
			if ( !container.countingAtStartOnly || atStart ) {
				if( container.visitsShouldBeCounted )
					state.IncrementVisitCountForContainer (container);

				if (container.turnIndexShouldBeCounted)
					state.RecordTurnIndexVisitToContainer (container);
			}
		}

		List<Container> _prevContainers = new List<Container>();
		void VisitChangedContainersDueToDivert()
		{
			var previousPointer = state.previousPointer;
			var pointer = state.currentPointer;

			// Unless we're pointing *directly* at a piece of content, we don't do
			// counting here. Otherwise, the main stepping function will do the counting.
			if (pointer.isNull || pointer.index == -1)
				return;
			
			// First, find the previously open set of containers
			_prevContainers.Clear();
			if (!previousPointer.isNull) {
				Container prevAncestor = previousPointer.Resolve() as Container ?? previousPointer.container as Container;
				while (prevAncestor) {
					_prevContainers.Add (prevAncestor);
					prevAncestor = prevAncestor.parent as Container;
				}
			}

			// If the new object is a container itself, it will be visited automatically at the next actual
			// content step. However, we need to walk up the new ancestry to see if there are more new containers
			Runtime.Object currentChildOfContainer = pointer.Resolve();

			// Invalid pointer? May happen if attemptingto 
			if (currentChildOfContainer == null) return;

			Container currentContainerAncestor = currentChildOfContainer.parent as Container;

			bool allChildrenEnteredAtStart = true;
			while (currentContainerAncestor && (!_prevContainers.Contains(currentContainerAncestor) || currentContainerAncestor.countingAtStartOnly)) {

				// Check whether this ancestor container is being entered at the start,
				// by checking whether the child object is the first.
				bool enteringAtStart = currentContainerAncestor.content.Count > 0 
					&& currentChildOfContainer == currentContainerAncestor.content [0]
					&& allChildrenEnteredAtStart;

				// Don't count it as entering at start if we're entering random somewhere within
				// a container B that happens to be nested at index 0 of container A. It only counts
				// if we're diverting directly to the first leaf node.
				if (!enteringAtStart)
					allChildrenEnteredAtStart = false;

				// Mark a visit to this container
				VisitContainer (currentContainerAncestor, enteringAtStart);

				currentChildOfContainer = currentContainerAncestor;
				currentContainerAncestor = currentContainerAncestor.parent as Container;
			}
		}
			
		Choice ProcessChoice(ChoicePoint choicePoint)
		{
			bool showChoice = true;

			// Don't create choice if choice point doesn't pass conditional
			if (choicePoint.hasCondition) {
				var conditionValue = state.PopEvaluationStack ();
				if (!IsTruthy (conditionValue)) {
					showChoice = false;
				}
			}

			string startText = "";
			string choiceOnlyText = "";

			if (choicePoint.hasChoiceOnlyContent) {
				var choiceOnlyStrVal = state.PopEvaluationStack () as StringValue;
				choiceOnlyText = choiceOnlyStrVal.value;
			}

			if (choicePoint.hasStartContent) {
				var startStrVal = state.PopEvaluationStack () as StringValue;
				startText = startStrVal.value;
			}

			// Don't create choice if player has already read this content
			if (choicePoint.onceOnly) {
				var visitCount = state.VisitCountForContainer (choicePoint.choiceTarget);
				if (visitCount > 0) {
					showChoice = false;
				}
			}

			// We go through the full process of creating the choice above so
			// that we consume the content for it, since otherwise it'll
			// be shown on the output stream.
			if (!showChoice) {
				return null;
			}

			var choice = new Choice ();
			choice.targetPath = choicePoint.pathOnChoice;
			choice.sourcePath = choicePoint.path.ToString ();
			choice.isInvisibleDefault = choicePoint.isInvisibleDefault;

			// We need to capture the state of the callstack at the point where
			// the choice was generated, since after the generation of this choice
			// we may go on to pop out from a tunnel (possible if the choice was
			// wrapped in a conditional), or we may pop out from a thread,
			// at which point that thread is discarded.
			// Fork clones the thread, gives it a new ID, but without affecting
			// the thread stack itself.
			choice.threadAtGeneration = state.callStack.ForkThread();

			// Set final text for the choice
			choice.text = (startText + choiceOnlyText).Trim(' ', '\t');

			return choice;
		}

		// Does the expression result represented by this object evaluate to true?
		// e.g. is it a Number that's not equal to 1?
		bool IsTruthy(Runtime.Object obj)
		{
			bool truthy = false;
			if (obj is Value) {
				var val = (Value)obj;

				if (val is DivertTargetValue) {
					var divTarget = (DivertTargetValue)val;
					Error ("Shouldn't use a divert target (to " + divTarget.targetPath + ") as a conditional value. Did you intend a function call 'likeThis()' or a read count check 'likeThis'? (no arrows)");
					return false;
				}

				return val.isTruthy;
			}
			return truthy;
		}

		/// <summary>
		/// Checks whether contentObj is a control or flow object rather than a piece of content, 
		/// and performs the required command if necessary.
		/// </summary>
		/// <returns><c>true</c> if object was logic or flow control, <c>false</c> if it's normal content.</returns>
		/// <param name="contentObj">Content object.</param>
		bool PerformLogicAndFlowControl(Runtime.Object contentObj)
		{
			if( contentObj == null ) {
				return false;
			}

			// Divert
			if (contentObj is Divert) {
				
				Divert currentDivert = (Divert)contentObj;

				if (currentDivert.isConditional) {
					var conditionValue = state.PopEvaluationStack ();

					// False conditional? Cancel divert
					if (!IsTruthy (conditionValue))
						return true;
				}

				if (currentDivert.hasVariableTarget) {
					var varName = currentDivert.variableDivertName;

					var varContents = state.variablesState.GetVariableWithName (varName);

					if (varContents == null) {
						Error ("Tried to divert using a target from a variable that could not be found (" + varName + ")");
					}
					else if (!(varContents is DivertTargetValue)) {

						var intContent = varContents as IntValue;

						string errorMessage = "Tried to divert to a target from a variable, but the variable (" + varName + ") didn't contain a divert target, it ";
						if (intContent && intContent.value == 0) {
							errorMessage += "was empty/null (the value 0).";
						} else {
							errorMessage += "contained '" + varContents + "'.";
						}

						Error (errorMessage);
					}

					var target = (DivertTargetValue)varContents;
					state.divertedPointer = PointerAtPath(target.targetPath);

				} else if (currentDivert.isExternal) {
					CallExternalFunction (currentDivert.targetPathString, currentDivert.externalArgs);
					return true;
				} else {
					state.divertedPointer = currentDivert.targetPointer;
				}

				if (currentDivert.pushesToStack) {
					state.callStack.Push (
						currentDivert.stackPushType, 
						outputStreamLengthWithPushed:state.outputStream.Count
					);
				}

				if (state.divertedPointer.isNull && !currentDivert.isExternal) {

					// Human readable name available - runtime divert is part of a hard-written divert that to missing content
					if (currentDivert && currentDivert.debugMetadata.sourceName != null) {
						Error ("Divert target doesn't exist: " + currentDivert.debugMetadata.sourceName);
					} else {
						Error ("Divert resolution failed: " + currentDivert);
					}
				}

				return true;
			} 

			// Start/end an expression evaluation? Or print out the result?
			else if( contentObj is ControlCommand ) {
				var evalCommand = (ControlCommand) contentObj;

				switch (evalCommand.commandType) {

				case ControlCommand.CommandType.EvalStart:
					Assert (state.inExpressionEvaluation == false, "Already in expression evaluation?");
					state.inExpressionEvaluation = true;
					break;

				case ControlCommand.CommandType.EvalEnd:
					Assert (state.inExpressionEvaluation == true, "Not in expression evaluation mode");
					state.inExpressionEvaluation = false;
					break;

				case ControlCommand.CommandType.EvalOutput:

					// If the expression turned out to be empty, there may not be anything on the stack
					if (state.evaluationStack.Count > 0) {
						
						var output = state.PopEvaluationStack ();

						// Functions may evaluate to Void, in which case we skip output
						if (!(output is Void)) {
							// TODO: Should we really always blanket convert to string?
							// It would be okay to have numbers in the output stream the
							// only problem is when exporting text for viewing, it skips over numbers etc.
							var text = new StringValue (output.ToString ());

							state.PushToOutputStream (text);
						}

					}
					break;

				case ControlCommand.CommandType.NoOp:
					break;

				case ControlCommand.CommandType.Duplicate:
					state.PushEvaluationStack (state.PeekEvaluationStack ());
					break;

				case ControlCommand.CommandType.PopEvaluatedValue:
					state.PopEvaluationStack ();
					break;

				case ControlCommand.CommandType.PopFunction:
				case ControlCommand.CommandType.PopTunnel:

					var popType = evalCommand.commandType == ControlCommand.CommandType.PopFunction ?
						PushPopType.Function : PushPopType.Tunnel;

					// Tunnel onwards is allowed to specify an optional override
					// divert to go to immediately after returning: ->-> target
					DivertTargetValue overrideTunnelReturnTarget = null;
					if (popType == PushPopType.Tunnel) {
						var popped = state.PopEvaluationStack ();
						overrideTunnelReturnTarget = popped as DivertTargetValue;
						if (overrideTunnelReturnTarget == null) {
							Assert (popped is Void, "Expected void if ->-> doesn't override target");
						}
					}

					if (state.TryExitFunctionEvaluationFromGame ()) {
						break;
					}
					else if (state.callStack.currentElement.type != popType || !state.callStack.canPop) {

						var names = new Dictionary<PushPopType, string> ();
						names [PushPopType.Function] = "function return statement (~ return)";
						names [PushPopType.Tunnel] = "tunnel onwards statement (->->)";

						string expected = names [state.callStack.currentElement.type];
						if (!state.callStack.canPop) {
							expected = "end of flow (-> END or choice)";
						}

						var errorMsg = string.Format ("Found {0}, when expected {1}", names [popType], expected);

						Error (errorMsg);
					} 

					else {
						state.PopCallstack ();

						// Does tunnel onwards override by diverting to a new ->-> target?
						if( overrideTunnelReturnTarget )
							state.divertedPointer = PointerAtPath (overrideTunnelReturnTarget.targetPath);
					}

					break;

				case ControlCommand.CommandType.BeginString:
					state.PushToOutputStream (evalCommand);

					Assert (state.inExpressionEvaluation == true, "Expected to be in an expression when evaluating a string");
					state.inExpressionEvaluation = false;
					break;

				case ControlCommand.CommandType.EndString:
					
					// Since we're iterating backward through the content,
					// build a stack so that when we build the string,
					// it's in the right order
					var contentStackForString = new Stack<Runtime.Object> ();

					int outputCountConsumed = 0;
					for (int i = state.outputStream.Count - 1; i >= 0; --i) {
						var obj = state.outputStream [i];

						outputCountConsumed++;

						var command = obj as ControlCommand;
						if (command != null && command.commandType == ControlCommand.CommandType.BeginString) {
							break;
						}

						if( obj is StringValue )
							contentStackForString.Push (obj);
					}

					// Consume the content that was produced for this string
					state.PopFromOutputStream (outputCountConsumed);

					// Build string out of the content we collected
					var sb = new StringBuilder ();
					foreach (var c in contentStackForString) {
						sb.Append (c.ToString ());
					}

					// Return to expression evaluation (from content mode)
					state.inExpressionEvaluation = true;
					state.PushEvaluationStack (new StringValue (sb.ToString ()));
					break;

				case ControlCommand.CommandType.ChoiceCount:
					var choiceCount = state.generatedChoices.Count;
					state.PushEvaluationStack (new Runtime.IntValue (choiceCount));
					break;

				case ControlCommand.CommandType.Turns:
					state.PushEvaluationStack (new IntValue (state.currentTurnIndex+1));
					break;

				case ControlCommand.CommandType.TurnsSince:
				case ControlCommand.CommandType.ReadCount:
					var target = state.PopEvaluationStack();
					if( !(target is DivertTargetValue) ) {
						string extraNote = "";
						if( target is IntValue )
							extraNote = ". Did you accidentally pass a read count ('knot_name') instead of a target ('-> knot_name')?";
						Error("TURNS_SINCE expected a divert target (knot, stitch, label name), but saw "+target+extraNote);
						break;
					}
						
					var divertTarget = target as DivertTargetValue;
					var container = ContentAtPath (divertTarget.targetPath).correctObj as Container;

					int eitherCount;
					if (container != null) {
						if (evalCommand.commandType == ControlCommand.CommandType.TurnsSince)
							eitherCount = state.TurnsSinceForContainer (container);
						else
							eitherCount = state.VisitCountForContainer (container);
					} else {
						if (evalCommand.commandType == ControlCommand.CommandType.TurnsSince)
							eitherCount = -1; // turn count, default to never/unknown
						else
							eitherCount = 0; // visit count, assume 0 to default to allowing entry

						Warning ("Failed to find container for " + evalCommand.ToString () + " lookup at " + divertTarget.targetPath.ToString ());
					}
					
					state.PushEvaluationStack (new IntValue (eitherCount));
					break;
					

				case ControlCommand.CommandType.Random: {
						var maxInt = state.PopEvaluationStack () as IntValue;
						var minInt = state.PopEvaluationStack () as IntValue;

						if (minInt == null)
							Error ("Invalid value for minimum parameter of RANDOM(min, max)");

						if (maxInt == null)
							Error ("Invalid value for maximum parameter of RANDOM(min, max)");

						// +1 because it's inclusive of min and max, for e.g. RANDOM(1,6) for a dice roll.
						int randomRange;
						try {
							randomRange = checked(maxInt.value - minInt.value + 1);
						} catch (System.OverflowException) {
							randomRange = int.MaxValue;
							Error("RANDOM was called with a range that exceeds the size that ink numbers can use.");
						}
						if (randomRange <= 0)
							Error ("RANDOM was called with minimum as " + minInt.value + " and maximum as " + maxInt.value + ". The maximum must be larger");

						var resultSeed = state.storySeed + state.previousRandom;
						var random = new Random (resultSeed);

						var nextRandom = random.Next ();
						var chosenValue = (nextRandom % randomRange) + minInt.value;
						state.PushEvaluationStack (new IntValue (chosenValue));

						// Next random number (rather than keeping the Random object around)
						state.previousRandom = nextRandom;
						break;
					}

				case ControlCommand.CommandType.SeedRandom:
					var seed = state.PopEvaluationStack () as IntValue;
					if (seed == null)
						Error ("Invalid value passed to SEED_RANDOM");

					// Story seed affects both RANDOM and shuffle behaviour
					state.storySeed = seed.value;
					state.previousRandom = 0;

					// SEED_RANDOM returns nothing.
					state.PushEvaluationStack (new Runtime.Void ());
					break;

				case ControlCommand.CommandType.VisitIndex:
					var count = state.VisitCountForContainer(state.currentPointer.container) - 1; // index not count
					state.PushEvaluationStack (new IntValue (count));
					break;

				case ControlCommand.CommandType.SequenceShuffleIndex:
					var shuffleIndex = NextSequenceShuffleIndex ();
					state.PushEvaluationStack (new IntValue (shuffleIndex));
					break;

				case ControlCommand.CommandType.StartThread:
					// Handled in main step function
					break;

				case ControlCommand.CommandType.Done:
					
					// We may exist in the context of the initial
					// act of creating the thread, or in the context of
					// evaluating the content.
					if (state.callStack.canPopThread) {
						state.callStack.PopThread ();
					} 

					// In normal flow - allow safe exit without warning
					else {
						state.didSafeExit = true;

						// Stop flow in current thread
						state.currentPointer = Pointer.Null;
					}

					break;
				
				// Force flow to end completely
				case ControlCommand.CommandType.End:
					state.ForceEnd ();
					break;

				case ControlCommand.CommandType.ListFromInt:
					var intVal = state.PopEvaluationStack () as IntValue;
					var listNameVal = state.PopEvaluationStack () as StringValue;

					if (intVal == null) { 
						throw new StoryException ("Passed non-integer when creating a list element from a numerical value."); 
					}

					ListValue generatedListValue = null;

					ListDefinition foundListDef;
					if (listDefinitions.TryListGetDefinition (listNameVal.value, out foundListDef)) {
						InkListItem foundItem;
						if (foundListDef.TryGetItemWithValue (intVal.value, out foundItem)) {
							generatedListValue = new ListValue (foundItem, intVal.value);
						}
					} else {
						throw new StoryException ("Failed to find LIST called " + listNameVal.value);
					}

					if (generatedListValue == null)
						generatedListValue = new ListValue ();

					state.PushEvaluationStack (generatedListValue);
					break;

				case ControlCommand.CommandType.ListRange: {
						var max = state.PopEvaluationStack () as Value;
						var min = state.PopEvaluationStack () as Value;

						var targetList = state.PopEvaluationStack () as ListValue;

						if (targetList == null || min == null || max == null)
							throw new StoryException ("Expected list, minimum and maximum for LIST_RANGE");

						var result = targetList.value.ListWithSubRange(min.valueObject, max.valueObject);

						state.PushEvaluationStack (new ListValue(result));
						break;
					}

				case ControlCommand.CommandType.ListRandom: {

						var listVal = state.PopEvaluationStack () as ListValue;
						if (listVal == null)
							throw new StoryException ("Expected list for LIST_RANDOM");
						
						var list = listVal.value;

						InkList newList = null;

						// List was empty: return empty list
						if (list.Count == 0) {
							newList = new InkList ();
						} 

						// Non-empty source list
						else {
							// Generate a random index for the element to take
							var resultSeed = state.storySeed + state.previousRandom;
							var random = new Random (resultSeed);

							var nextRandom = random.Next ();
							var listItemIndex = nextRandom % list.Count;

							// Iterate through to get the random element
							var listEnumerator = list.GetEnumerator ();
							for (int i = 0; i <= listItemIndex; i++) {
								listEnumerator.MoveNext ();
							}
							var randomItem = listEnumerator.Current;

							// Origin list is simply the origin of the one element
							newList = new InkList (randomItem.Key.originName, this);
							newList.Add (randomItem.Key, randomItem.Value);

							state.previousRandom = nextRandom;
						}

						state.PushEvaluationStack (new ListValue(newList));
						break;
					}

				default:
					Error ("unhandled ControlCommand: " + evalCommand);
					break;
				}

				return true;
			}

			// Variable assignment
			else if( contentObj is VariableAssignment ) {
				var varAss = (VariableAssignment) contentObj;
				var assignedVal = state.PopEvaluationStack();

				// When in temporary evaluation, don't create new variables purely within
				// the temporary context, but attempt to create them globally
				//var prioritiseHigherInCallStack = _temporaryEvaluationContainer != null;

				state.variablesState.Assign (varAss, assignedVal);

				return true;
			}

			// Variable reference
			else if( contentObj is VariableReference ) {
				var varRef = (VariableReference)contentObj;
				Runtime.Object foundValue = null;


				// Explicit read count value
				if (varRef.pathForCount != null) {

					var container = varRef.containerForCount;
					int count = state.VisitCountForContainer (container);
					foundValue = new IntValue (count);
				}

				// Normal variable reference
				else {

					foundValue = state.variablesState.GetVariableWithName (varRef.name);

					if (foundValue == null) {
						Warning ("Variable not found: '" + varRef.name + "'. Using default value of 0 (false). This can happen with temporary variables if the declaration hasn't yet been hit. Globals are always given a default value on load if a value doesn't exist in the save state.");
						foundValue = new IntValue (0);
					}
				}

				state.PushEvaluationStack (foundValue);

				return true;
			}

			// Native function call
			else if (contentObj is NativeFunctionCall) {
				var func = (NativeFunctionCall)contentObj;
				var funcParams = state.PopEvaluationStack (func.numberOfParameters);
				var result = func.Call (funcParams);
				state.PushEvaluationStack (result);
				return true;
			} 

			// No control content, must be ordinary content
			return false;
		}

		/// <summary>
		/// Change the current position of the story to the given path. From here you can 
		/// call Continue() to evaluate the next line.
		/// 
		/// The path string is a dot-separated path as used internally by the engine.
		/// These examples should work:
		/// 
		///    myKnot
		///    myKnot.myStitch
		/// 
		/// Note however that this won't necessarily work:
		/// 
		///    myKnot.myStitch.myLabelledChoice
		/// 
		/// ...because of the way that content is nested within a weave structure.
		/// 
		/// By default this will reset the callstack beforehand, which means that any
		/// tunnels, threads or functions you were in at the time of calling will be
		/// discarded. This is different from the behaviour of ChooseChoiceIndex, which
		/// will always keep the callstack, since the choices are known to come from the
		/// correct state, and known their source thread.
		/// 
		/// You have the option of passing false to the resetCallstack parameter if you
		/// don't want this behaviour, and will leave any active threads, tunnels or
		/// function calls in-tact.
		/// 
		/// This is potentially dangerous! If you're in the middle of a tunnel,
		/// it'll redirect only the inner-most tunnel, meaning that when you tunnel-return
		/// using '->->', it'll return to where you were before. This may be what you
		/// want though. However, if you're in the middle of a function, ChoosePathString
		/// will throw an exception.
		/// 
		/// </summary>
		/// <param name="path">A dot-separted path string, as specified above.</param>
		/// <param name="resetCallstack">Whether to reset the callstack first (see summary description).</param>
		/// <param name="arguments">Optional set of arguments to pass, if path is to a knot that takes them.</param>
		public void ChoosePathString (string path, bool resetCallstack = true, params object [] arguments)
		{
			IfAsyncWeCant ("call ChoosePathString right now");
			if(onChoosePathString != null) onChoosePathString(path, arguments);
			if (resetCallstack) {
				ResetCallstack ();
			} else {
				// ChoosePathString is potentially dangerous since you can call it when the stack is
				// pretty much in any state. Let's catch one of the worst offenders.
				if (state.callStack.currentElement.type == PushPopType.Function) {
					string funcDetail = "";
					var container = state.callStack.currentElement.currentPointer.container;
					if (container != null) {
						funcDetail = "("+container.path.ToString ()+") ";
					}
					throw new System.Exception ("Story was running a function "+funcDetail+"when you called ChoosePathString("+path+") - this is almost certainly not not what you want! Full stack trace: \n"+state.callStack.callStackTrace);
				}
			}

			state.PassArgumentsToEvaluationStack (arguments);
			ChoosePath (new Path (path));
		}

		void IfAsyncWeCant (string activityStr)
		{
			if (_asyncContinueActive)
				throw new System.Exception ("Can't " + activityStr + ". Story is in the middle of a ContinueAsync(). Make more ContinueAsync() calls or a single Continue() call beforehand.");
		}
			
		public void ChoosePath(Path p, bool incrementingTurnIndex = true)
		{
			state.SetChosenPath (p, incrementingTurnIndex);

			// Take a note of newly visited containers for read counts etc
			VisitChangedContainersDueToDivert ();
		}

		/// <summary>
		/// Chooses the Choice from the currentChoices list with the given
		/// index. Internally, this sets the current content path to that
		/// pointed to by the Choice, ready to continue story evaluation.
		/// </summary>
		public void ChooseChoiceIndex(int choiceIdx)
		{
			var choices = currentChoices;
			Assert (choiceIdx >= 0 && choiceIdx < choices.Count, "choice out of range");

			// Replace callstack with the one from the thread at the choosing point, 
			// so that we can jump into the right place in the flow.
			// This is important in case the flow was forked by a new thread, which
			// can create multiple leading edges for the story, each of
			// which has its own context.
			var choiceToChoose = choices [choiceIdx];
			if(onMakeChoice != null) onMakeChoice(choiceToChoose);
			state.callStack.currentThread = choiceToChoose.threadAtGeneration;

			ChoosePath (choiceToChoose.targetPath);
		}

		/// <summary>
		/// Checks if a function exists.
		/// </summary>
		/// <returns>True if the function exists, else false.</returns>
		/// <param name="functionName">The name of the function as declared in ink.</param>
		public bool HasFunction (string functionName)
		{
			try {
				return KnotContainerWithName (functionName) != null;
			} catch {
				return false;
			}
		}

		/// <summary>
		/// Evaluates a function defined in ink.
		/// </summary>
		/// <returns>The return value as returned from the ink function with `~ return myValue`, or null if nothing is returned.</returns>
		/// <param name="functionName">The name of the function as declared in ink.</param>
		/// <param name="arguments">The arguments that the ink function takes, if any. Note that we don't (can't) do any validation on the number of arguments right now, so make sure you get it right!</param>
		public object EvaluateFunction (string functionName, params object [] arguments)
		{
			string _;
			return EvaluateFunction (functionName, out _, arguments);
		}

		/// <summary>
		/// Evaluates a function defined in ink, and gathers the possibly multi-line text as generated by the function.
		/// This text output is any text written as normal content within the function, as opposed to the return value, as returned with `~ return`.
		/// </summary>
		/// <returns>The return value as returned from the ink function with `~ return myValue`, or null if nothing is returned.</returns>
		/// <param name="functionName">The name of the function as declared in ink.</param>
		/// <param name="textOutput">The text content produced by the function via normal ink, if any.</param>
		/// <param name="arguments">The arguments that the ink function takes, if any. Note that we don't (can't) do any validation on the number of arguments right now, so make sure you get it right!</param>
		public object EvaluateFunction (string functionName, out string textOutput, params object [] arguments)
		{
			if(onEvaluateFunction != null) onEvaluateFunction(functionName, arguments);
			IfAsyncWeCant ("evaluate a function");

			if(functionName == null) {
				throw new System.Exception ("Function is null");
			} else if(functionName == string.Empty || functionName.Trim() == string.Empty) {
				throw new System.Exception ("Function is empty or white space.");
			}

			// Get the content that we need to run
			var funcContainer = KnotContainerWithName (functionName);
			if( funcContainer == null )
				throw new System.Exception ("Function doesn't exist: '" + functionName + "'");

			// Snapshot the output stream
			var outputStreamBefore = new List<Runtime.Object>(state.outputStream);
			_state.ResetOutput ();

			// State will temporarily replace the callstack in order to evaluate
			state.StartFunctionEvaluationFromGame (funcContainer, arguments);

			// Evaluate the function, and collect the string output
			var stringOutput = new StringBuilder ();
			while (canContinue) {
				stringOutput.Append (Continue ());
			}
			textOutput = stringOutput.ToString ();

			// Restore the output stream in case this was called
			// during main story evaluation.
			_state.ResetOutput (outputStreamBefore);

			// Finish evaluation, and see whether anything was produced
			var result = state.CompleteFunctionEvaluationFromGame ();
			if(onCompleteEvaluateFunction != null) onCompleteEvaluateFunction(functionName, arguments, textOutput, result);
			return result;
		}

		// Evaluate a "hot compiled" piece of ink content, as used by the REPL-like
		// CommandLinePlayer.
		public Runtime.Object EvaluateExpression(Runtime.Container exprContainer)
		{
			int startCallStackHeight = state.callStack.elements.Count;

			state.callStack.Push (PushPopType.Tunnel);

			_temporaryEvaluationContainer = exprContainer;

			state.GoToStart ();

			int evalStackHeight = state.evaluationStack.Count;

			Continue ();

			_temporaryEvaluationContainer = null;

			// Should have fallen off the end of the Container, which should
			// have auto-popped, but just in case we didn't for some reason,
			// manually pop to restore the state (including currentPath).
			if (state.callStack.elements.Count > startCallStackHeight) {
				state.PopCallstack ();
			}

			int endStackHeight = state.evaluationStack.Count;
			if (endStackHeight > evalStackHeight) {
				return state.PopEvaluationStack ();
			} else {
				return null;
			}

		}

		/// <summary>
		/// An ink file can provide a fallback functions for when when an EXTERNAL has been left
		/// unbound by the client, and the fallback function will be called instead. Useful when
		/// testing a story in playmode, when it's not possible to write a client-side C# external
		/// function, but you don't want it to fail to run.
		/// </summary>
		public bool allowExternalFunctionFallbacks { get; set; }

		public void CallExternalFunction(string funcName, int numberOfArguments)
		{
			ExternalFunctionDef funcDef;
			Container fallbackFunctionContainer = null;

			var foundExternal = _externals.TryGetValue (funcName, out funcDef);

			// Should this function break glue? Abort run if we've already seen a newline.
			// Set a bool to tell it to restore the snapshot at the end of this instruction.
			if( foundExternal && !funcDef.lookaheadSafe && _stateSnapshotAtLastNewline != null ) {
				_sawLookaheadUnsafeFunctionAfterNewline = true;
				return;
			}

			// Try to use fallback function?
			if (!foundExternal) {
				if (allowExternalFunctionFallbacks) {
					fallbackFunctionContainer = KnotContainerWithName (funcName);
					Assert (fallbackFunctionContainer != null, "Trying to call EXTERNAL function '" + funcName + "' which has not been bound, and fallback ink function could not be found.");

					// Divert direct into fallback function and we're done
					state.callStack.Push (
						PushPopType.Function, 
						outputStreamLengthWithPushed:state.outputStream.Count
					);
					state.divertedPointer = Pointer.StartOf(fallbackFunctionContainer);
					return;

				} else {
					Assert (false, "Trying to call EXTERNAL function '" + funcName + "' which has not been bound (and ink fallbacks disabled).");
				}
			}

			// Pop arguments
			var arguments = new List<object>();
			for (int i = 0; i < numberOfArguments; ++i) {
				var poppedObj = state.PopEvaluationStack () as Value;
				var valueObj = poppedObj.valueObject;
				arguments.Add (valueObj);
			}

			// Reverse arguments from the order they were popped,
			// so they're the right way round again.
			arguments.Reverse ();

			// Run the function!
			object funcResult = funcDef.function (arguments.ToArray());

			// Convert return value (if any) to the a type that the ink engine can use
			Runtime.Object returnObj = null;
			if (funcResult != null) {
				returnObj = Value.Create (funcResult);
				Assert (returnObj != null, "Could not create ink value from returned object of type " + funcResult.GetType());
			} else {
				returnObj = new Runtime.Void ();
			}
				
			state.PushEvaluationStack (returnObj);
		}

		/// <summary>
		/// General purpose delegate definition for bound EXTERNAL function definitions
		/// from ink. Note that this version isn't necessary if you have a function
		/// with three arguments or less - see the overloads of BindExternalFunction.
		/// </summary>
		public delegate object ExternalFunction(object[] args);

		/// <summary>
		/// Most general form of function binding that returns an object
		/// and takes an array of object parameters.
		/// The only way to bind a function with more than 3 arguments.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunctionGeneral(string funcName, ExternalFunction func, bool lookaheadSafe = true)
		{
			IfAsyncWeCant ("bind an external function");
			Assert (!_externals.ContainsKey (funcName), "Function '" + funcName + "' has already been bound.");
			_externals [funcName] = new ExternalFunctionDef {
				function = func,
				lookaheadSafe = lookaheadSafe
			};
		}

		object TryCoerce<T>(object value)
		{  
			if (value == null)
				return null;

			if (value is T)
				return (T) value;

			if (value is float && typeof(T) == typeof(int)) {
				int intVal = (int)Math.Round ((float)value);
				return intVal;
			}

			if (value is int && typeof(T) == typeof(float)) {
				float floatVal = (float)(int)value;
				return floatVal;
			}

			if (value is int && typeof(T) == typeof(bool)) {
				int intVal = (int)value;
				return intVal == 0 ? false : true;
			}

			if (value is bool && typeof(T) == typeof(int)) {
				bool boolVal = (bool)value;
				return boolVal ? 1 : 0;
			}

			if (typeof(T) == typeof(string)) {
				return value.ToString ();
			}

			Assert (false, "Failed to cast " + value.GetType ().Name + " to " + typeof(T).Name);

			return null;
		}

		// Convenience overloads for standard functions and actions of various arities
		// Is there a better way of doing this?!

		/// <summary>
		/// Bind a C# function to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction(string funcName, Func<object> func, bool lookaheadSafe=false)
		{
			Assert(func != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 0, "External function expected no arguments");
				return func();
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# Action to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="act">The C# action to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction(string funcName, Action act, bool lookaheadSafe=false)
		{
			Assert(act != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 0, "External function expected no arguments");
				act();
				return null;
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# function to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T>(string funcName, Func<T, object> func, bool lookaheadSafe=false)
		{
			Assert(func != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 1, "External function expected one argument");
				return func( (T)TryCoerce<T>(args[0]) );
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# action to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="act">The C# action to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T>(string funcName, Action<T> act, bool lookaheadSafe=false)
		{
			Assert(act != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 1, "External function expected one argument");
				act( (T)TryCoerce<T>(args[0]) );
				return null;
			}, lookaheadSafe);
		}


		/// <summary>
		/// Bind a C# function to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2>(string funcName, Func<T1, T2, object> func, bool lookaheadSafe = false)
		{
			Assert(func != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 2, "External function expected two arguments");
				return func(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1])
				);
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# action to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="act">The C# action to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2>(string funcName, Action<T1, T2> act, bool lookaheadSafe=false)
		{
			Assert(act != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 2, "External function expected two arguments");
				act(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1])
				);
				return null;
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# function to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2, T3>(string funcName, Func<T1, T2, T3, object> func, bool lookaheadSafe=false)
		{
			Assert(func != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 3, "External function expected three arguments");
				return func(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1]),
					(T3)TryCoerce<T3>(args[2])
				);
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# action to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="act">The C# action to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2, T3>(string funcName, Action<T1, T2, T3> act, bool lookaheadSafe=false)
		{
			Assert(act != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 3, "External function expected three arguments");
				act(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1]),
					(T3)TryCoerce<T3>(args[2])
				);
				return null;
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# function to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="func">The C# function to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2, T3, T4>(string funcName, Func<T1, T2, T3, T4, object> func, bool lookaheadSafe=false)
		{
			Assert(func != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 4, "External function expected four arguments");
				return func(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1]),
					(T3)TryCoerce<T3>(args[2]),
					(T4)TryCoerce<T4>(args[3])
				);
			}, lookaheadSafe);
		}

		/// <summary>
		/// Bind a C# action to an ink EXTERNAL function declaration.
		/// </summary>
		/// <param name="funcName">EXTERNAL ink function name to bind to.</param>
		/// <param name="act">The C# action to bind.</param>
		/// <param name="lookaheadSafe">The ink engine often evaluates further 
		/// than you might expect beyond the current line just in case it sees 
		/// glue that will cause the two lines to become one. In this case it's 
		/// possible that a function can appear to be called twice instead of 
		/// just once, and earlier than you expect. If it's safe for your 
		/// function to be called in this way (since the result and side effect 
		/// of the function will not change), then you can pass 'true'. 
		/// Usually, you want to pass 'false', especially if you want some action 
		/// to be performed in game code when this function is called.</param>
		public void BindExternalFunction<T1, T2, T3, T4>(string funcName, Action<T1, T2, T3, T4> act, bool lookaheadSafe=false)
		{
			Assert(act != null, "Can't bind a null function");

			BindExternalFunctionGeneral (funcName, (object[] args) => {
				Assert(args.Length == 4, "External function expected four arguments");
				act(
					(T1)TryCoerce<T1>(args[0]), 
					(T2)TryCoerce<T2>(args[1]),
					(T3)TryCoerce<T3>(args[2]),
					(T4)TryCoerce<T4>(args[3])
				);
				return null;
			}, lookaheadSafe);
		}
		
		/// <summary>
		/// Remove a binding for a named EXTERNAL ink function.
		/// </summary>
		public void UnbindExternalFunction(string funcName)
		{
			IfAsyncWeCant ("unbind an external a function");
			Assert (_externals.ContainsKey (funcName), "Function '" + funcName + "' has not been bound.");
			_externals.Remove (funcName);
		}

		/// <summary>
		/// Check that all EXTERNAL ink functions have a valid bound C# function.
		/// Note that this is automatically called on the first call to Continue().
		/// </summary>
		public void ValidateExternalBindings()
		{
			var missingExternals = new HashSet<string>();

			ValidateExternalBindings (_mainContentContainer, missingExternals);
			_hasValidatedExternals = true;

			// No problem! Validation complete
			if( missingExternals.Count == 0 ) {
				_hasValidatedExternals = true;
			} 

			// Error for all missing externals
			else {
				var message = string.Format("ERROR: Missing function binding for external{0}: '{1}' {2}",
					missingExternals.Count > 1 ? "s" : string.Empty,
					string.Join("', '", missingExternals.ToArray()),
					allowExternalFunctionFallbacks ? ", and no fallback ink function found." : " (ink fallbacks disabled)"
				);
					
				Error(message);
			}
		}

		void ValidateExternalBindings(Container c, HashSet<string> missingExternals)
		{
			foreach (var innerContent in c.content) {
				var container = innerContent as Container;
				if( container == null || !container.hasValidName )
					ValidateExternalBindings (innerContent, missingExternals);
			}
			foreach (var innerKeyValue in c.namedContent) {
				ValidateExternalBindings (innerKeyValue.Value as Runtime.Object, missingExternals);
			}
		}

		void ValidateExternalBindings(Runtime.Object o, HashSet<string> missingExternals)
		{
			var container = o as Container;
			if (container) {
				ValidateExternalBindings (container, missingExternals);
				return;
			}

			var divert = o as Divert;
			if (divert && divert.isExternal) {
				var name = divert.targetPathString;

				if (!_externals.ContainsKey (name)) {
					if( allowExternalFunctionFallbacks ) {
						bool fallbackFound = mainContentContainer.namedContent.ContainsKey(name);
						if( !fallbackFound ) {
							missingExternals.Add(name);
						}
					} else {
						missingExternals.Add(name);
					}
				}
			}
		}
		   
		/// <summary>
		/// Delegate definition for variable observation - see ObserveVariable.
		/// </summary>
		public delegate void VariableObserver(string variableName, object newValue);

		/// <summary>
		/// When the named global variable changes it's value, the observer will be
		/// called to notify it of the change. Note that if the value changes multiple
		/// times within the ink, the observer will only be called once, at the end
		/// of the ink's evaluation. If, during the evaluation, it changes and then
		/// changes back again to its original value, it will still be called.
		/// Note that the observer will also be fired if the value of the variable
		/// is changed externally to the ink, by directly setting a value in
		/// story.variablesState.
		/// </summary>
		/// <param name="variableName">The name of the global variable to observe.</param>
		/// <param name="observer">A delegate function to call when the variable changes.</param>
		public void ObserveVariable(string variableName, VariableObserver observer)
		{
			IfAsyncWeCant ("observe a new variable");

			if (_variableObservers == null)
				_variableObservers = new Dictionary<string, VariableObserver> ();

			if( !state.variablesState.GlobalVariableExistsWithName(variableName) ) 
				throw new Exception("Cannot observe variable '"+variableName+"' because it wasn't declared in the ink story.");

			if (_variableObservers.ContainsKey (variableName)) {
				_variableObservers[variableName] += observer;
			} else {
				_variableObservers[variableName] = observer;
			}
		}

		/// <summary>
		/// Convenience function to allow multiple variables to be observed with the same
		/// observer delegate function. See the singular ObserveVariable for details.
		/// The observer will get one call for every variable that has changed.
		/// </summary>
		/// <param name="variableNames">The set of variables to observe.</param>
		/// <param name="observer">The delegate function to call when any of the named variables change.</param>
		public void ObserveVariables(IList<string> variableNames, VariableObserver observer)
		{
			foreach (var varName in variableNames) {
				ObserveVariable (varName, observer);
			}
		}

		/// <summary>
		/// Removes the variable observer, to stop getting variable change notifications.
		/// If you pass a specific variable name, it will stop observing that particular one. If you
		/// pass null (or leave it blank, since it's optional), then the observer will be removed
		/// from all variables that it's subscribed to. If you pass in a specific variable name and
		/// null for the the observer, all observers for that variable will be removed. 
		/// </summary>
		/// <param name="observer">(Optional) The observer to stop observing.</param>
		/// <param name="specificVariableName">(Optional) Specific variable name to stop observing.</param>
		public void RemoveVariableObserver(VariableObserver observer = null, string specificVariableName = null)
		{
			IfAsyncWeCant ("remove a variable observer");

			if (_variableObservers == null)
				return;

			// Remove observer for this specific variable
			if (specificVariableName != null) {
				if (_variableObservers.ContainsKey (specificVariableName)) {
					if( observer != null) {
						_variableObservers [specificVariableName] -= observer;
						if (_variableObservers[specificVariableName] == null) {
							_variableObservers.Remove(specificVariableName);
						}
					}
					else {
						_variableObservers.Remove(specificVariableName);
					}
				}
			} 

			// Remove observer for all variables
			else if( observer != null) {
				var keys = new List<string>(_variableObservers.Keys);
				foreach (var varName in keys) {
					_variableObservers[varName] -= observer;
					if (_variableObservers[varName] == null) {
						_variableObservers.Remove(varName);
					}
				}
			}
		}

		void VariableStateDidChangeEvent(string variableName, Runtime.Object newValueObj)
		{
			if (_variableObservers == null)
				return;
			
			VariableObserver observers = null;
			if (_variableObservers.TryGetValue (variableName, out observers)) {

				if (!(newValueObj is Value)) {
					throw new System.Exception ("Tried to get the value of a variable that isn't a standard type");
				}
				var val = newValueObj as Value;

				observers (variableName, val.valueObject);
			}
		}

		/// <summary>
		/// Get any global tags associated with the story. These are defined as
		/// hash tags defined at the very top of the story.
		/// </summary>
		public List<string> globalTags {
			get {
				return TagsAtStartOfFlowContainerWithPathString ("");
			}
		}

		/// <summary>
		/// Gets any tags associated with a particular knot or knot.stitch.
		/// These are defined as hash tags defined at the very top of a 
		/// knot or stitch.
		/// </summary>
		/// <param name="path">The path of the knot or stitch, in the form "knot" or "knot.stitch".</param>
		public List<string> TagsForContentAtPath (string path)
		{
			return TagsAtStartOfFlowContainerWithPathString (path);
		}

		List<string> TagsAtStartOfFlowContainerWithPathString (string pathString)
		{
			var path = new Runtime.Path (pathString);

			// Expected to be global story, knot or stitch
			var flowContainer = ContentAtPath (path).container;
			while(true) {
				var firstContent = flowContainer.content [0];
				if (firstContent is Container)
					flowContainer = (Container)firstContent;
				else break;
			}

			// Any initial tag objects count as the "main tags" associated with that story/knot/stitch
			List<string> tags = null;
			foreach (var c in flowContainer.content) {
				var tag = c as Runtime.Tag;
				if (tag) {
					if (tags == null) tags = new List<string> ();
					tags.Add (tag.text);
				} else break;
			}

			return tags;
		}

		/// <summary>
		/// Useful when debugging a (very short) story, to visualise the state of the
		/// story. Add this call as a watch and open the extended text. A left-arrow mark
		/// will denote the current point of the story.
		/// It's only recommended that this is used on very short debug stories, since
		/// it can end up generate a large quantity of text otherwise.
		/// </summary>
		public virtual string BuildStringOfHierarchy()
		{
			var sb = new StringBuilder ();

			mainContentContainer.BuildStringOfHierarchy (sb, 0, state.currentPointer.Resolve());

			return sb.ToString ();
		}

		string BuildStringOfContainer (Container container)
		{
			var sb = new StringBuilder ();

			container.BuildStringOfHierarchy (sb, 0, state.currentPointer.Resolve());

			return sb.ToString();
		}

		private void NextContent()
		{
			// Setting previousContentObject is critical for VisitChangedContainersDueToDivert
			state.previousPointer = state.currentPointer;

			// Divert step?
			if (!state.divertedPointer.isNull) {

				state.currentPointer = state.divertedPointer;
				state.divertedPointer = Pointer.Null;

				// Internally uses state.previousContentObject and state.currentContentObject
				VisitChangedContainersDueToDivert ();

				// Diverted location has valid content?
				if (!state.currentPointer.isNull) {
					return;
				}
				
				// Otherwise, if diverted location doesn't have valid content,
				// drop down and attempt to increment.
				// This can happen if the diverted path is intentionally jumping
				// to the end of a container - e.g. a Conditional that's re-joining
			}

			bool successfulPointerIncrement = IncrementContentPointer ();

			// Ran out of content? Try to auto-exit from a function,
			// or finish evaluating the content of a thread
			if (!successfulPointerIncrement) {

				bool didPop = false;

				if (state.callStack.CanPop (PushPopType.Function)) {

					// Pop from the call stack
					state.PopCallstack (PushPopType.Function);

					// This pop was due to dropping off the end of a function that didn't return anything,
					// so in this case, we make sure that the evaluator has something to chomp on if it needs it
					if (state.inExpressionEvaluation) {
						state.PushEvaluationStack (new Runtime.Void ());
					}

					didPop = true;
				} else if (state.callStack.canPopThread) {
					state.callStack.PopThread ();

					didPop = true;
				} else {
					state.TryExitFunctionEvaluationFromGame ();
				}

				// Step past the point where we last called out
				if (didPop && !state.currentPointer.isNull) {
					NextContent ();
				}
			}
		}

		bool IncrementContentPointer()
		{
			bool successfulIncrement = true;

			var pointer = state.callStack.currentElement.currentPointer;
			pointer.index++;

			// Each time we step off the end, we fall out to the next container, all the
			// while we're in indexed rather than named content
			while (pointer.index >= pointer.container.content.Count) {

				successfulIncrement = false;

				Container nextAncestor = pointer.container.parent as Container;
				if (!nextAncestor) {
					break;
				}

				var indexInAncestor = nextAncestor.content.IndexOf (pointer.container);
				if (indexInAncestor == -1) {
					break;
				}

				pointer = new Pointer (nextAncestor, indexInAncestor);

				// Increment to next content in outer container
				pointer.index++;

				successfulIncrement = true;
			}

			if (!successfulIncrement) pointer = Pointer.Null;

			state.callStack.currentElement.currentPointer = pointer;

			return successfulIncrement;
		}
			
		bool TryFollowDefaultInvisibleChoice()
		{
			var allChoices = _state.currentChoices;

			// Is a default invisible choice the ONLY choice?
			var invisibleChoices = allChoices.Where (c => c.isInvisibleDefault).ToList();
			if (invisibleChoices.Count == 0 || allChoices.Count > invisibleChoices.Count)
				return false;

			var choice = invisibleChoices [0];

			// Invisible choice may have been generated on a different thread,
			// in which case we need to restore it before we continue
			state.callStack.currentThread = choice.threadAtGeneration;

			// If there's a chance that this state will be rolled back to before
			// the invisible choice then make sure that the choice thread is
			// left intact, and it isn't re-entered in an old state.
			if ( _stateSnapshotAtLastNewline != null )
				state.callStack.currentThread = state.callStack.ForkThread();

			ChoosePath (choice.targetPath, incrementingTurnIndex: false);

			return true;
		}
			

		// Note that this is O(n), since it re-evaluates the shuffle indices
		// from a consistent seed each time.
		// TODO: Is this the best algorithm it can be?
		int NextSequenceShuffleIndex()
		{
			var numElementsIntVal = state.PopEvaluationStack () as IntValue;
			if (numElementsIntVal == null) {
				Error ("expected number of elements in sequence for shuffle index");
				return 0;
			}

			var seqContainer = state.currentPointer.container;

			int numElements = numElementsIntVal.value;

			var seqCountVal = state.PopEvaluationStack () as IntValue;
			var seqCount = seqCountVal.value;
			var loopIndex = seqCount / numElements;
			var iterationIndex = seqCount % numElements;

			// Generate the same shuffle based on:
			//  - The hash of this container, to make sure it's consistent
			//    each time the runtime returns to the sequence
			//  - How many times the runtime has looped around this full shuffle
			var seqPathStr = seqContainer.path.ToString();
			int sequenceHash = 0;
			foreach (char c in seqPathStr) {
				sequenceHash += c;
			}
			var randomSeed = sequenceHash + loopIndex + state.storySeed;
			var random = new Random (randomSeed);

			var unpickedIndices = new List<int> ();
			for (int i = 0; i < numElements; ++i) {
				unpickedIndices.Add (i);
			}

			for (int i = 0; i <= iterationIndex; ++i) {
				var chosen = random.Next () % unpickedIndices.Count;
				var chosenIndex = unpickedIndices [chosen];
				unpickedIndices.RemoveAt (chosen);

				if (i == iterationIndex) {
					return chosenIndex;
				}
			}

			throw new System.Exception ("Should never reach here");
		}

		// Throw an exception that gets caught and causes AddError to be called,
		// then exits the flow.
		public void Error(string message, bool useEndLineNumber = false)
		{
			var e = new StoryException (message);
			e.useEndLineNumber = useEndLineNumber;
			throw e;
		}

		public void Warning (string message)
		{
			AddError (message, isWarning:true);
		}

		void AddError (string message, bool isWarning = false, bool useEndLineNumber = false)
		{
			var dm = currentDebugMetadata;

			var errorTypeStr = isWarning ? "WARNING" : "ERROR";

			if (dm != null) {
				int lineNum = useEndLineNumber ? dm.endLineNumber : dm.startLineNumber;
				message = string.Format ("RUNTIME {0}: '{1}' line {2}: {3}", errorTypeStr, dm.fileName, lineNum, message);
			} else if( !state.currentPointer.isNull  ) {
				message = string.Format ("RUNTIME {0}: ({1}): {2}", errorTypeStr, state.currentPointer.path, message);
			} else {
				message = "RUNTIME "+errorTypeStr+": " + message;
			}

			state.AddError (message, isWarning);

			// In a broken state don't need to know about any other errors.
			if( !isWarning )
				state.ForceEnd ();
		}

		void Assert(bool condition, string message = null, params object[] formatParams)
		{
			if (condition == false) {
				if (message == null) {
					message = "Story assert";
				}
				if (formatParams != null && formatParams.Count() > 0) {
					message = string.Format (message, formatParams);
				}
					
				throw new System.Exception (message + " " + currentDebugMetadata);
			}
		}

		DebugMetadata currentDebugMetadata
		{
			get {
				DebugMetadata dm;

				// Try to get from the current path first
				var pointer = state.currentPointer;
				if (!pointer.isNull) {
					dm = pointer.Resolve().debugMetadata;
					if (dm != null) {
						return dm;
					}
				}
					
				// Move up callstack if possible
				for (int i = state.callStack.elements.Count - 1; i >= 0; --i) {
					pointer = state.callStack.elements [i].currentPointer;
					if (!pointer.isNull && pointer.Resolve() != null) {
						dm = pointer.Resolve().debugMetadata;
						if (dm != null) {
							return dm;
						}
					}
				}

				// Current/previous path may not be valid if we've just had an error,
				// or if we've simply run out of content.
				// As a last resort, try to grab something from the output stream
				for (int i = state.outputStream.Count - 1; i >= 0; --i) {
					var outputObj = state.outputStream [i];
					dm = outputObj.debugMetadata;
					if (dm != null) {
						return dm;
					}
				}

				return null;
			}
		}

		int currentLineNumber 
		{
			get {
				var dm = currentDebugMetadata;
				if (dm != null) {
					return dm.startLineNumber;
				}
				return 0;
			}
		}

		public Container mainContentContainer {
			get {
				if (_temporaryEvaluationContainer) {
					return _temporaryEvaluationContainer;
				} else {
					return _mainContentContainer;
				}
			}
		}

		Container _mainContentContainer;
		ListDefinitionsOrigin _listDefinitions;

		struct ExternalFunctionDef {
			public ExternalFunction function;
			public bool lookaheadSafe;
		}
		Dictionary<string, ExternalFunctionDef> _externals;
		Dictionary<string, VariableObserver> _variableObservers;
		bool _hasValidatedExternals;

		Container _temporaryEvaluationContainer;

		StoryState _state;

		bool _asyncContinueActive;
		StoryState _stateSnapshotAtLastNewline = null;
		bool _sawLookaheadUnsafeFunctionAfterNewline = false;

		int _recursiveContinueCount = 0;

		bool _asyncSaving;

		Profiler _profiler;
	}
	public class InkList : Dictionary<InkListItem, int>{
		/// <summary>
		/// Create a new empty ink list.
		/// </summary>
		public InkList () { }

		/// <summary>
		/// Create a new ink list that contains the same contents as another list.
		/// </summary>
		public InkList(InkList otherList) : base(otherList)
		{
			_originNames = otherList.originNames;
			if (otherList.origins != null)
			{
				origins = new List<ListDefinition>(otherList.origins);
			}
		}

		/// <summary>
		/// Create a new empty ink list that's intended to hold items from a particular origin
		/// list definition. The origin Story is needed in order to be able to look up that definition.
		/// </summary>
		public InkList (string singleOriginListName, Story originStory)
		{
			SetInitialOriginName (singleOriginListName);

			ListDefinition def;
			if (originStory.listDefinitions.TryListGetDefinition (singleOriginListName, out def))
				origins = new List<ListDefinition> { def };
			else
				throw new System.Exception ("InkList origin could not be found in story when constructing new list: " + singleOriginListName);
		}

		public InkList (KeyValuePair<InkListItem, int> singleElement)
		{
			Add (singleElement.Key, singleElement.Value);
		}

		/// <summary>
		/// Converts a string to an ink list and returns for use in the story.
		/// </summary>
		/// <returns>InkList created from string list item</returns>
		/// <param name="itemKey">Item key.</param>
		/// <param name="originStory">Origin story.</param>
		public static InkList FromString(string myListItem, Story originStory) {
			var listValue = originStory.listDefinitions.FindSingleItemListWithName (myListItem);
			if (listValue)
				return new InkList (listValue.value);
			else 
				throw new System.Exception ("Could not find the InkListItem from the string '" + myListItem + "' to create an InkList because it doesn't exist in the original list definition in ink.");
		}


		/// <summary>
		/// Adds the given item to the ink list. Note that the item must come from a list definition that
		/// is already "known" to this list, so that the item's value can be looked up. By "known", we mean
		/// that it already has items in it from that source, or it did at one point - it can't be a 
		/// completely fresh empty list, or a list that only contains items from a different list definition.
		/// </summary>
		public void AddItem (InkListItem item)
		{
			if (item.originName == null) {
				AddItem (item.itemName);
				return;
			}
			
			foreach (var origin in origins) {
				if (origin.name == item.originName) {
					int intVal;
					if (origin.TryGetValueForItem (item, out intVal)) {
						this [item] = intVal;
						return;
					} else {
						throw new System.Exception ("Could not add the item " + item + " to this list because it doesn't exist in the original list definition in ink.");
					}
				}
			}

			throw new System.Exception ("Failed to add item to list because the item was from a new list definition that wasn't previously known to this list. Only items from previously known lists can be used, so that the int value can be found.");
		}

		/// <summary>
		/// Adds the given item to the ink list, attempting to find the origin list definition that it belongs to.
		/// The item must therefore come from a list definition that is already "known" to this list, so that the
		/// item's value can be looked up. By "known", we mean that it already has items in it from that source, or
		/// it did at one point - it can't be a completely fresh empty list, or a list that only contains items from
		/// a different list definition.
		/// </summary>
		public void AddItem (string itemName)
		{
			ListDefinition foundListDef = null;

			foreach (var origin in origins) {
				if (origin.ContainsItemWithName (itemName)) {
					if (foundListDef != null) {
						throw new System.Exception ("Could not add the item " + itemName + " to this list because it could come from either " + origin.name + " or " + foundListDef.name);
					} else {
						foundListDef = origin;
					}
				}
			}

			if (foundListDef == null)
				throw new System.Exception ("Could not add the item " + itemName + " to this list because it isn't known to any list definitions previously associated with this list.");

			var item = new InkListItem (foundListDef.name, itemName);
			var itemVal = foundListDef.ValueForItem(item);
			this [item] = itemVal;
		}

		/// <summary>
		/// Returns true if this ink list contains an item with the given short name
		/// (ignoring the original list where it was defined).
		/// </summary>
		public bool ContainsItemNamed (string itemName)
		{
			foreach (var itemWithValue in this) {
				if (itemWithValue.Key.itemName == itemName) return true;
			}
			return false;
		}

		// Story has to set this so that the value knows its origin,
		// necessary for certain operations (e.g. interacting with ints).
		// Only the story has access to the full set of lists, so that
		// the origin can be resolved from the originListName.
		public List<ListDefinition> origins;
		public ListDefinition originOfMaxItem {
			get {
				if (origins == null) return null;

				var maxOriginName = maxItem.Key.originName;
				foreach (var origin in origins) {
					if (origin.name == maxOriginName)
						return origin;
				}

				return null;
			}
		}

		// Origin name needs to be serialised when content is empty,
		// assuming a name is availble, for list definitions with variable
		// that is currently empty.
		public List<string> originNames {
			get {
				if (this.Count > 0) {
					if (_originNames == null && this.Count > 0)
						_originNames = new List<string> ();
					else
						_originNames.Clear ();

					foreach (var itemAndValue in this)
						_originNames.Add (itemAndValue.Key.originName);
				}

				return _originNames;
			}
		}
		List<string> _originNames;

		public void SetInitialOriginName (string initialOriginName)
		{
			_originNames = new List<string> { initialOriginName };
		}

		public void SetInitialOriginNames (List<string> initialOriginNames)
		{
			if (initialOriginNames == null)
				_originNames = null;
			else
				_originNames = new List<string>(initialOriginNames);
		}

		/// <summary>
		/// Get the maximum item in the list, equivalent to calling LIST_MAX(list) in ink.
		/// </summary>
		public KeyValuePair<InkListItem, int> maxItem {
			get {
				KeyValuePair<InkListItem, int> max = new KeyValuePair<InkListItem, int>();
				foreach (var kv in this) {
					if (max.Key.isNull || kv.Value > max.Value)
						max = kv;
				}
				return max;
			}
		}

		/// <summary>
		/// Get the minimum item in the list, equivalent to calling LIST_MIN(list) in ink.
		/// </summary>
		public KeyValuePair<InkListItem, int> minItem {
			get {
				var min = new KeyValuePair<InkListItem, int> ();
				foreach (var kv in this) {
					if (min.Key.isNull || kv.Value < min.Value)
						min = kv;
				}
				return min;
			}
		}

		/// <summary>
		/// The inverse of the list, equivalent to calling LIST_INVERSE(list) in ink
		/// </summary>
		public InkList inverse {
			get {
				var list = new InkList ();
				if (origins != null) {
					foreach (var origin in origins) {
						foreach (var itemAndValue in origin.items) {
							if (!this.ContainsKey (itemAndValue.Key))
								list.Add (itemAndValue.Key, itemAndValue.Value);
						}
					}

				}
				return list;
			}
		}

		/// <summary>
		/// The list of all items from the original list definition, equivalent to calling
		/// LIST_ALL(list) in ink.
		/// </summary>
		public InkList all {
			get {
				var list = new InkList ();
				if (origins != null) {
					foreach (var origin in origins) {
						foreach (var itemAndValue in origin.items)
							list[itemAndValue.Key] = itemAndValue.Value;
					}
				}
				return list;
			}
		}

		/// <summary>
		/// Returns a new list that is the combination of the current list and one that's
		/// passed in. Equivalent to calling (list1 + list2) in ink.
		/// </summary>
		public InkList Union (InkList otherList)
		{
			var union = new InkList (this);
			foreach (var kv in otherList) {
				union [kv.Key] = kv.Value;
			}
			return union;
		}

		/// <summary>
		/// Returns a new list that is the intersection of the current list with another
		/// list that's passed in - i.e. a list of the items that are shared between the
		/// two other lists. Equivalent to calling (list1 ^ list2) in ink.
		/// </summary>
		public InkList Intersect (InkList otherList)
		{
			var intersection = new InkList ();
			foreach (var kv in this) {
				if (otherList.ContainsKey (kv.Key))
					intersection.Add (kv.Key, kv.Value);
			}
			return intersection;
		}

		/// <summary>
		/// Returns a new list that's the same as the current one, except with the given items
		/// removed that are in the passed in list. Equivalent to calling (list1 - list2) in ink.
		/// </summary>
		/// <param name="listToRemove">List to remove.</param>
		public InkList Without (InkList listToRemove)
		{
			var result = new InkList (this);
			foreach (var kv in listToRemove)
				result.Remove (kv.Key);
			return result;
		}

		/// <summary>
		/// Returns true if the current list contains all the items that are in the list that
		/// is passed in. Equivalent to calling (list1 ? list2) in ink.
		/// </summary>
		/// <param name="otherList">Other list.</param>
		public bool Contains (InkList otherList)
		{
			foreach (var kv in otherList) {
				if (!this.ContainsKey (kv.Key)) return false;
			}
			return true;
		}

		/// <summary>
		/// Returns true if all the item values in the current list are greater than all the
		/// item values in the passed in list. Equivalent to calling (list1 > list2) in ink.
		/// </summary>
		public bool GreaterThan (InkList otherList)
		{
			if (Count == 0) return false;
			if (otherList.Count == 0) return true;

			// All greater
			return minItem.Value > otherList.maxItem.Value;
		}

		/// <summary>
		/// Returns true if the item values in the current list overlap or are all greater than
		/// the item values in the passed in list. None of the item values in the current list must
		/// fall below the item values in the passed in list. Equivalent to (list1 >= list2) in ink,
		/// or LIST_MIN(list1) >= LIST_MIN(list2) &amp;&amp; LIST_MAX(list1) >= LIST_MAX(list2).
		/// </summary>
		public bool GreaterThanOrEquals (InkList otherList)
		{
			if (Count == 0) return false;
			if (otherList.Count == 0) return true;

			return minItem.Value >= otherList.minItem.Value
				&& maxItem.Value >= otherList.maxItem.Value;
		}

		/// <summary>
		/// Returns true if all the item values in the current list are less than all the
		/// item values in the passed in list. Equivalent to calling (list1 &lt; list2) in ink.
		/// </summary>
		public bool LessThan (InkList otherList)
		{
			if (otherList.Count == 0) return false;
			if (Count == 0) return true;

			return maxItem.Value < otherList.minItem.Value;
		}

		/// <summary>
		/// Returns true if the item values in the current list overlap or are all less than
		/// the item values in the passed in list. None of the item values in the current list must
		/// go above the item values in the passed in list. Equivalent to (list1 &lt;= list2) in ink,
		/// or LIST_MAX(list1) &lt;= LIST_MAX(list2) &amp;&amp; LIST_MIN(list1) &lt;= LIST_MIN(list2).
		/// </summary>
		public bool LessThanOrEquals (InkList otherList)
		{
			if (otherList.Count == 0) return false;
			if (Count == 0) return true;

			return maxItem.Value <= otherList.maxItem.Value
				&& minItem.Value <= otherList.minItem.Value;
		}

		public InkList MaxAsList ()
		{
			if (Count > 0)
				return new InkList (maxItem);
			else
				return new InkList ();
		}

		public InkList MinAsList ()
		{
			if (Count > 0)
				return new InkList (minItem);
			else
				return new InkList ();
		}

		/// <summary>
		/// Returns a sublist with the elements given the minimum and maxmimum bounds.
		/// The bounds can either be ints which are indices into the entire (sorted) list,
		/// or they can be InkLists themselves. These are intended to be single-item lists so
		/// you can specify the upper and lower bounds. If you pass in multi-item lists, it'll
		/// use the minimum and maximum items in those lists respectively.
		/// WARNING: Calling this method requires a full sort of all the elements in the list.
		/// </summary>
		public InkList ListWithSubRange(object minBound, object maxBound) 
		{
			if (this.Count == 0) return new InkList();

			var ordered = orderedItems;

			int minValue = 0;
			int maxValue = int.MaxValue;

			if (minBound is int)
			{
				minValue = (int)minBound;
			}

			else
			{
				if( minBound is InkList && ((InkList)minBound).Count > 0 )
					minValue = ((InkList)minBound).minItem.Value;
			}

			if (maxBound is int)
				maxValue = (int)maxBound;
			else 
			{
				if (minBound is InkList && ((InkList)minBound).Count > 0)
					maxValue = ((InkList)maxBound).maxItem.Value;
			}

			var subList = new InkList();
			subList.SetInitialOriginNames(originNames);
			foreach(var item in ordered) {
				if( item.Value >= minValue && item.Value <= maxValue ) {
					subList.Add(item.Key, item.Value);
				}
			}

			return subList;
		}

		/// <summary>
		/// Returns true if the passed object is also an ink list that contains
		/// the same items as the current list, false otherwise.
		/// </summary>
		public override bool Equals (object other)
		{
			var otherRawList = other as InkList;
			if (otherRawList == null) return false;
			if (otherRawList.Count != Count) return false;

			foreach (var kv in this) {
				if (!otherRawList.ContainsKey (kv.Key))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Return the hashcode for this object, used for comparisons and inserting into dictionaries.
		/// </summary>
		public override int GetHashCode ()
		{
			int ownHash = 0;
			foreach (var kv in this)
				ownHash += kv.Key.GetHashCode ();
			return ownHash;
		}

		List<KeyValuePair<InkListItem, int>> orderedItems {
			get {
				var ordered = new List<KeyValuePair<InkListItem, int>>();
				ordered.AddRange(this);
				ordered.Sort((x, y) => {
					// Ensure consistent ordering of mixed lists.
					if( x.Value == y.Value ) {
						return x.Key.originName.CompareTo(y.Key.originName);
					} else {
						return x.Value.CompareTo(y.Value);
					}
				});
				return ordered;
			}
		}

		/// <summary>
		/// Returns a string in the form "a, b, c" with the names of the items in the list, without
		/// the origin list definition names. Equivalent to writing {list} in ink.
		/// </summary>
		public override string ToString ()
		{
			var ordered = orderedItems;

			var sb = new StringBuilder ();
			for (int i = 0; i < ordered.Count; i++) {
				if (i > 0)
					sb.Append (", ");

				var item = ordered [i].Key;
				sb.Append (item.itemName);
			}

			return sb.ToString ();
		}
	}
	public class ListValue : Value<InkList>{
		public override ValueType valueType {
			get {
				return ValueType.List;
			}
		}

		// Truthy if it is non-empty
		public override bool isTruthy {
			get {
				return value.Count > 0;
			}
		}
				
		public override Value Cast (ValueType newType)
		{
			if (newType == ValueType.Int) {
				var max = value.maxItem;
				if( max.Key.isNull )
					return new IntValue (0);
				else
					return new IntValue (max.Value);
			}

			else if (newType == ValueType.Float) {
				var max = value.maxItem;
				if (max.Key.isNull)
					return new FloatValue (0.0f);
				else
					return new FloatValue ((float)max.Value);
			}

			else if (newType == ValueType.String) {
				var max = value.maxItem;
				if (max.Key.isNull)
					return new StringValue ("");
				else {
					return new StringValue (max.Key.ToString());
				}
			}

			if (newType == valueType)
				return this;

			throw BadCastException (newType);
		}

		public ListValue () : base(null) {
			value = new InkList ();
		}

		public ListValue (InkList list) : base (null)
		{
			value = new InkList (list);
		}

		public ListValue (InkListItem singleItem, int singleValue) : base (null)
		{
			value = new InkList {
				{singleItem, singleValue}
			};
		}

		public static void RetainListOriginsForAssignment (Runtime.Object oldValue, Runtime.Object newValue)
		{
			var oldList = oldValue as ListValue;
			var newList = newValue as ListValue;

			// When assigning the emtpy list, try to retain any initial origin names
			if (oldList && newList && newList.value.Count == 0)
				newList.value.SetInitialOriginNames (oldList.value.originNames);
		}
	}
	public class ListDefinitionsOrigin{
		public List<Runtime.ListDefinition> lists {
			get {
				var listOfLists = new List<Runtime.ListDefinition> ();
				foreach (var namedList in _lists) {
					listOfLists.Add (namedList.Value);
				}
				return listOfLists;
			}
		}

		public ListDefinitionsOrigin (List<Runtime.ListDefinition> lists)
		{
			_lists = new Dictionary<string, ListDefinition> ();
			_allUnambiguousListValueCache = new Dictionary<string, ListValue>();

			foreach (var list in lists) {
				_lists [list.name] = list;

				foreach(var itemWithValue in list.items) {
					var item = itemWithValue.Key;
					var val = itemWithValue.Value;
					var listValue = new ListValue(item, val);

					// May be ambiguous, but compiler should've caught that,
					// so we may be doing some replacement here, but that's okay.
					_allUnambiguousListValueCache[item.itemName] = listValue;
					_allUnambiguousListValueCache[item.fullName] = listValue;
				}
			}
		}

		public bool TryListGetDefinition (string name, out ListDefinition def)
		{
			return _lists.TryGetValue (name, out def);
		}

		public ListValue FindSingleItemListWithName (string name)
		{
			ListValue val = null;
			_allUnambiguousListValueCache.TryGetValue(name, out val);
			return val;
		}

		Dictionary<string, Runtime.ListDefinition> _lists;
		Dictionary<string, ListValue> _allUnambiguousListValueCache;
	}
 	public class VariableReference : Object{
		// Normal named variable
		public string name { get; set; }

		// Variable reference is actually a path for a visit (read) count
		public Path pathForCount { get; set; }

		public Container containerForCount {
			get {
				return this.ResolvePath (pathForCount).container;
			}
		}
			
		public string pathStringForCount { 
			get {
				if( pathForCount == null )
					return null;

				return CompactPathString(pathForCount);
			}
			set {
				if (value == null)
					pathForCount = null;
				else
					pathForCount = new Path (value);
			}
		}

		public VariableReference (string name)
		{
			this.name = name;
		}

		// Require default constructor for serialisation
		public VariableReference() {}

		public override string ToString ()
		{
			if (name != null) {
				return string.Format ("var({0})", name);
			} else {
				var pathStr = pathStringForCount;
				return string.Format("read_count({0})", pathStr);
			}
	}
	}
}
