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
		public string Name { get; private set; }
		
		readonly Dictionary<string, PropertyInfo> _props = new Dictionary<string, PropertyInfo>();
		
		public ResourceTypeInfo (Type type)
		{
			Name = type.Name;
			
			LearnAboutType (type);
		}
		
		void LearnAboutType (Type type)
		{
			foreach (var p in type.GetProperties (System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
				if (!p.CanWrite) return;
				if (p.GetIndexParameters ().Length != 0) return;
				_props [p.Name] = p;
			}
		}
		
		public IEnumerable<PropertyInfo> Properties {
			get {
				return _props.Values;
			}
		}
	}
	
	public class Resource : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		
		protected virtual void OnPropertyChanged (string name)
		{
			var ev = PropertyChanged;
			if (ev != null) {
				ev (this, new PropertyChangedEventArgs (name));
			}
		}
		
		public virtual string Url {
			get {
				return "/" + GetType ().Name;
			}
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
				.ToArray ();
		}
	}
	
	public abstract class Query
	{
		static object _queryTypeInfoLock = new object ();
		static readonly Dictionary<Type, QueryTypeInfo> _queryTypeInfo = new Dictionary<Type, QueryTypeInfo>();
		
		public virtual QueryTypeInfo TypeInfo
		{
			get {
				QueryTypeInfo info;
				var type = GetType ();
				lock (_queryTypeInfoLock) {
					if (!_queryTypeInfo.TryGetValue (type, out info)) {
						info = new QueryTypeInfo (type);
						_queryTypeInfo.Add (type, info);
					}
				}
				return info;
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
		List<object> _items;
		
		public int Count { get { return _items.Count; } }
		public object this [int index] { get { return _items [index]; } }
		
		public QueryResult (System.Collections.IEnumerable enumerable)
		{
			_items = new List<object> (enumerable.Cast<object> ());
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

				ResourceTypeInfo info = null;
				foreach (var i in _items) {
					if (info == null) {
						info = new ResourceTypeInfo (i.GetType ());
					}
					w.WriteStartElement (info.Name);
					foreach (var p in info.Properties) {
						var v = p.GetValue (i, null);
						var s = string.Format (cult, "{0}", v);
						w.WriteElementString (p.Name, s);
					}
					w.WriteEndElement ();
				}
				
				w.WriteEndElement ();
			}
		}
	}
	
	public class QueryResult<T>
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
			var url = MakeUrlAbsolute (resource.Url);
			
			var req = (HttpWebRequest)WebRequest.Create (url);
			req.CookieContainer = _cookies;
			
			return Task.Factory
				.FromAsync<WebResponse> (req.BeginGetResponse, req.EndGetResponse, null)
				.ContinueWith ((resTask) => {
					if (resTask.IsFaulted) throw resTask.Exception.InnerException;
					throw new NotImplementedException ();
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
		
		public Task<QueryResult<T>> Get<T> (Query query) where T : Resource
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

