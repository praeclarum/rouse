using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Rouse
{
	#region Resources
	
	public class PrimaryKeyAttribute : Attribute
	{
	}

	public class ResourceTypeInfo
	{
		public readonly string Name;
		public readonly PropertyInfo[] Properties;
		
		public ResourceTypeInfo (Type type)
		{
			Name = type.Name;
			Properties = type
				.GetProperties (BindingFlags.Public | BindingFlags.Instance)
				.Where (x => x.CanWrite)
				.Where (x => x.GetIndexParameters ().Length == 0)
				.ToArray ();
		}
	}
	
	public abstract class Resource : INotifyPropertyChanged
	{
		static object _typeInfosLock = new object ();
		static readonly Dictionary<Type, ResourceTypeInfo> _typeInfos = new Dictionary<Type, ResourceTypeInfo> ();
		
		ResourceTypeInfo _typeInfo = null;
		
		public ResourceTypeInfo TypeInfo
		{
			get {
				if (_typeInfo == null) {
					var type = GetType ();
					lock (_typeInfosLock) {
						if (!_typeInfos.TryGetValue (type, out _typeInfo)) {
							_typeInfo = new ResourceTypeInfo (type);
							_typeInfos.Add (type, _typeInfo);
						}
					}
				}
				return _typeInfo;
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
		
		protected virtual void OnPropertyChanged (string name)
		{
			var ev = PropertyChanged;
			if (ev != null) {
				ev (this, new PropertyChangedEventArgs (name));
			}
		}
		
		public virtual string Path {
			get {
				return "/" + TypeInfo.Name;
			}
		}
		
		public void ReadXml (System.IO.Stream stream)
		{
			using (var reader = new System.Xml.XmlTextReader (stream)) {
				ReadXml (reader);
			}
		}
		
		public virtual void ReadXml (System.Xml.XmlReader reader)
		{
			throw new NotImplementedException ();
		}
		
		public virtual void WriteXml (System.Xml.XmlWriter writer)
		{
			var cult = System.Globalization.CultureInfo.InvariantCulture;
			var info = TypeInfo;
			writer.WriteStartElement (info.Name);
			foreach (var p in info.Properties) {
				var v = p.GetValue (this, null);
				var s = string.Format (cult, "{0}", v);
				writer.WriteElementString (p.Name, s);
			}
			writer.WriteEndElement ();
		}
	}
	
	#endregion
	
	#region Queries
	
	public class QueryTypeInfo
	{
		public readonly string Name;
		public readonly PropertyInfo[] Parameters;
		
		public QueryTypeInfo (Type type)
		{
			Name = type.Name;
			Parameters = type
				.GetProperties (BindingFlags.Public | BindingFlags.Instance)
				.Where (x => x.CanWrite)
				.Where (x => x.GetIndexParameters ().Length == 0)
				.ToArray ();
		}
	}
	
	public abstract class Query
	{
		static object _typeInfosLock = new object ();
		static readonly Dictionary<Type, QueryTypeInfo> _typeInfos = new Dictionary<Type, QueryTypeInfo> ();

		QueryTypeInfo _typeInfo = null;
		
		public QueryTypeInfo TypeInfo
		{
			get {
				if (_typeInfo == null) {
					var type = GetType ();
					lock (_typeInfosLock) {
						if (!_typeInfos.TryGetValue (type, out _typeInfo)) {
							_typeInfo = new QueryTypeInfo (type);
							_typeInfos.Add (type, _typeInfo);
						}
					}
				}
				return _typeInfo;
			}
		}
		
		public virtual string Path
		{
			get {
				return "/" + TypeInfo.Name;
			}
		}
		
		public virtual string Url
		{
			get {
				var sb = new StringBuilder ();
				sb.Append (Path);				
				var info = TypeInfo;
				var head = "?";
				foreach (var p in info.Parameters) {
					sb.Append (head);
					sb.Append (p.Name);
					sb.Append ('=');
					sb.Append (Uri.EscapeDataString (p.GetValue (this, null).ToString ()));
					head = "&";
				}
				return sb.ToString ();
			}
		}
		
		public abstract System.Collections.IEnumerable Get (ICollectionFactory collections);
	}
	
	public interface ICollectionFactory
	{
		IRepositoryCollection<T> Get<T> ();
	}
	
	public interface IRepositoryCollection<T> : IQueryable<T>
	{
	}
	
	public class QueryResult
	{
		List<Resource> _items;
		
		public int Count { get { return _items.Count; } }
		public Resource this [int index] { get { return _items [index]; } }
		
		public QueryResult (System.Collections.IEnumerable enumerable)
		{
			_items = new List<Resource> (enumerable.Cast<Resource> ());
		}
		
		public static QueryResult FromXml (System.IO.TextReader textReader)
		{
			using (var xmlReader = new System.Xml.XmlTextReader (textReader)) {
				return FromXml (xmlReader);
			}
		}
		
		public static QueryResult FromXml (System.Xml.XmlReader reader)
		{
			throw new NotImplementedException ();
		}
		
		public void WriteContent (string contentType, System.IO.Stream stream, Encoding encoding)
		{
			switch (contentType) {
			case "application/xml":
				WriteXml (stream, encoding);
				break;
			default:
				throw new NotSupportedException ("Content-Type: " + contentType);
			}
		}
		
		public void WriteXml (System.IO.Stream stream, Encoding encoding)
		{
			var cult = System.Globalization.CultureInfo.InvariantCulture;
			
			var settings = new System.Xml.XmlWriterSettings ();
			settings.Indent = true;
			settings.Encoding = encoding;
			
			using (var w = System.Xml.XmlWriter.Create (stream, settings)) {
				w.WriteStartDocument ();
				w.WriteStartElement ("result");
				w.WriteAttributeString ("count", Count.ToString (cult));

				foreach (var i in _items) {
					i.WriteXml (w);
				}
				
				w.WriteEndElement ();
			}
		}
	}
	
	public class QueryResult<T>
		where T : Resource
	{
		QueryResult _result;
		
		public int Count { get { return _result.Count; } }
		
		public QueryResult (QueryResult result)
		{
			_result = result;
		}
		
		public T this [int index] {
			get {
				return (T)_result [index];
			}
		}
	}
	
	#endregion
	
	#region Repositories
		
	public class CacheRepository : Repository
	{
		class CacheItem
		{
			public QueryResult Result;			
			public bool NeedsRefresh { get { return true; } }
		}
		
		readonly Repository _sourceRepository;
		
		readonly object _cacheLock = new object ();
		readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem> ();
		
		public CacheRepository (Repository sourceRepository)
		{
			if (sourceRepository == null) throw new ArgumentNullException ("sourceRepository");
			_sourceRepository = sourceRepository;
		}
		
		public override Task Save (Resource resource)
		{
			return _sourceRepository.Save (resource);
		}
		
		public override Task<QueryResult> Get (Query query)
		{
			var url = query.Url;
			
			return Task.Factory.StartNew (delegate {
				
				var item = default (CacheItem);
				
				lock (_cacheLock) {
					_cache.TryGetValue (url, out item);
				}
				
				if (item != null) {
					return item.Result;
				}
				else {
					var sourceTask = _sourceRepository
						.Get (query)
						.ContinueWith ((task) => {
							if (task.Exception != null) throw task.Exception.InnerException;
							var r = task.Result;
							lock (_cacheLock) {
								_cache [url] = new CacheItem {
									Result = r,
								};
							}
							return r;
						});
					return sourceTask.Result;
				}
			});
		}
	}
	
	public class HttpRepository : Repository
	{
		string _hostname;
		CookieContainer _cookies;	
		
		public HttpRepository ()
		{
			_hostname = "";
			_cookies = new CookieContainer ();
		}
		
		public HttpRepository (string hostname)
		{
			_hostname = hostname;
		}
		
		string MakeUrlAbsolute (string url)
		{
			if (!url.StartsWith ("http://") && !url.StartsWith ("https://")) {
				if (!url.StartsWith ("/")) {
					url = "/" + url;
				}
				url = "http://" + _hostname + url;
			}
			return url;
		}
		
		public override Task Save (Resource resource)
		{
			var url = MakeUrlAbsolute (resource.Path);
			
			var req = (HttpWebRequest)WebRequest.Create (url);
			req.Method = "POST";
			req.CookieContainer = _cookies;
			req.ContentType = "application/xml";
			var encoding = Encoding.UTF8;
			
			return Task.Factory
				.FromAsync<System.IO.Stream> (req.BeginGetRequestStream, req.EndGetRequestStream, null)
				.ContinueWith ((streamTask) => {
					if (streamTask.IsFaulted) throw streamTask.Exception.InnerException;
					
					var mem = new System.IO.MemoryStream ();
					using (var w = new System.Xml.XmlTextWriter (mem, encoding)) {
						w.WriteStartDocument ();
						resource.WriteXml (w);
					}
					req.ContentLength = mem.Length;
					mem.Position = 0;
					using (var reqStream = streamTask.Result) {
						mem.CopyTo (reqStream);
					}
					var t = Task.Factory
						.FromAsync<WebResponse> (req.BeginGetResponse, req.EndGetResponse, null)
						.ContinueWith ((resTask) => {
							if (resTask.IsFaulted) throw resTask.Exception.InnerException;
							throw new NotImplementedException ();
						});
					t.Wait ();
					if (t.IsFaulted) throw t.Exception.InnerException;
				});
		}
		
		public override Task<QueryResult> Get (Query query)
		{
			var url = MakeUrlAbsolute (query.Url);
			
			var req = (HttpWebRequest)WebRequest.Create (url);
			req.CookieContainer = _cookies;
			
			return Task.Factory
				.FromAsync<WebResponse> (req.BeginGetResponse, req.EndGetResponse, null)
				.ContinueWith ((resTask) => {
					if (resTask.IsFaulted) throw resTask.Exception.InnerException;
					var res = (HttpWebResponse)resTask.Result;
					using (var s = res.GetResponseStream ()) {
						using (var r = new System.IO.StreamReader (s)) {
							return QueryResult.FromXml (r);
						}
					}
				});
		}
	}
	
	public abstract class Repository
	{
		//public abstract Task<T> Get<T> (object primaryKey);
		
		public abstract Task Save (Resource resource);
		
		public abstract Task<QueryResult> Get (Query query);
		
		public Task<QueryResult<T>> Get<T> (Query query)
			where T : Resource
		{
			return Get ((Query)query)
				.ContinueWith ((task) => {
					if (task.Exception != null) throw task.Exception.InnerException;
					return new QueryResult<T> (task.Result);
				});
		}
	}
	
	#endregion
}

