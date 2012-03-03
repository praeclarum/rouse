using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Rouse.Data
{
	public class DataRepository : Repository
	{
		IDbConnection _connection;
		readonly object _connectionLock = new object ();
		
		Collections _collections;
		
		public IDbConnection Connection { get { return _connection; } }
		
		public DataRepository (IDbConnection connection)
		{
			if (connection == null) throw new ArgumentNullException ("connection");
			_connection = connection;
			
			_collections = new Collections (this);
		}
		
		class Collections : ICollectionFactory
		{
			DataRepository _repo;
			
			public Collections (DataRepository repo)
			{
				_repo = repo;
			}
			
			public IRepositoryCollection<T> Get<T> ()
			{
				return new Collection<T> (_repo);
			}
		}
		
		class OrderBy
		{
			public string Text;
			public string Direction;
		}
		
		class Parameter
		{
			public string Name;
			public object Value;
		}
		
		class Collection<T> : IRepositoryCollection<T>, IOrderedQueryable<T>
		{
			DataRepository _repo;
			
			public Type ElementType { get { return typeof(T); } }
			
			public Collection (Expression expression, DataRepository repo)
			{
				_repo = repo;
				Expression = expression;
				Provider = new CollectionQueryProvider<T> (this);
			}
			
			public Collection (DataRepository repo)
			{
				_repo = repo;
				Expression = Expression.Constant (this);
				Provider = new CollectionQueryProvider<T> (this);
			}
			
			public Expression Expression { get; private set; }
			
			public IQueryProvider Provider { get; private set; }
			
			public IEnumerator<T> GetEnumerator ()
			{
				List<T> r = new List<T>();
				
				if (_repo._connection.State != ConnectionState.Open) {
					_repo._connection.Open ();
				}
				
				var tableInfo = _repo.GetTableInfo (typeof (T));
				
				using (var cmd = _repo._connection.CreateCommand ()) {
					
					cmd.CommandText = GetSqlCommandText (tableInfo);
					
					foreach (var p in _params) {
						var dbp = cmd.CreateParameter ();
						dbp.ParameterName = p.Name;
						dbp.Value = p.Value;
						cmd.Parameters.Add (dbp);
					}
					
					using (var reader = cmd.ExecuteReader ()) {
						
						object[] vals = null;
						System.Reflection.PropertyInfo[] fields = null;
						
						while (reader.Read ()) {
							
							if (vals == null) {
								vals = new object [reader.FieldCount];
								fields = new System.Reflection.PropertyInfo [vals.Length];
								for (var i = 0; i < fields.Length; i++) {
									var name = reader.GetName (i);
									fields [i] = tableInfo.GetColumn (name).Property;
								}
							}
							
							reader.GetValues (vals);
							var obj = Activator.CreateInstance <T> ();
							for (var i = 0; i < fields.Length; i++) {
								fields [i].SetValue (obj, vals [i], null);
							}
							r.Add (obj);
						}
					}
				}
				
				return r.GetEnumerator ();
			}
			
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
			{
				return ((IEnumerable<T>)this).GetEnumerator ();
			}
			
			public string GetSqlCommandText (TableInfo table)
			{
				var sb = new StringBuilder ();
				
				sb.AppendFormat ("select {0} from \"{1}\"", _select, table.Name);
				
				var head = " where ";
				foreach (var w in _wheres) {
					sb.Append (head);
					sb.Append (w);
					head = " and ";
				}
				
				head = " order by ";
				foreach (var o in _orderbys) {
					sb.Append (head);
					sb.Append (o.Text);
					sb.Append (' ');
					sb.Append (o.Direction);
					head = ",";
				}
				
				if (!string.IsNullOrEmpty (_take)) {
					sb.Append (" limit ");
					sb.Append (_take);
				}
				
				return sb.ToString ();
			}
			
			List<Parameter> _params = new List<Parameter> ();
			List<string> _wheres = new List<string> ();
			List<OrderBy> _orderbys = new List<OrderBy> ();
			string _take;
			string _select = "*";
			
			public Collection<U> Clone<U> (Expression accExpression)
			{
				var coll = new Collection<U> (accExpression, _repo);
				coll._params.AddRange (_params);
				coll._wheres.AddRange (_wheres);
				coll._orderbys.AddRange (_orderbys);
				coll._take = _take;
				coll._select = _select;
				return coll;
			}
			
			public void ApplyWhere (Expression expression)
			{
				_wheres.Add (CompileExpression (expression));
			}
			
			public void ApplyOrderByDescending (Expression expression)
			{
				_orderbys.Add (new OrderBy {
					Text = CompileExpression (expression),
					Direction = "desc",
				});
			}
			
			public void ApplyTake (Expression expression)
			{
				_take = CompileExpression (expression);
			}
			
			string CompileParam (object value)
			{
				var index = _params.Count;
				var p = new Parameter {
					Name = ":p" + index,
					Value = value,
				};
				_params.Add (p);
				return p.Name;
			}
			
			string CompileExpression (Expression expr)
			{
				switch (expr.NodeType)
				{
				case ExpressionType.Constant:
				{
					var conste = (ConstantExpression)expr;
					if (expr.Type == typeof(string)) {
						return CompileParam (conste.Value);
					}
					else {
						return string.Format (System.Globalization.CultureInfo.InvariantCulture, "{0}", conste.Value);
					}
				}
				case ExpressionType.Equal:
				{
					var bine = (BinaryExpression)expr;
					return "(" + CompileExpression (bine.Left) + "=" + CompileExpression (bine.Right) + ")";
				}
				case ExpressionType.MemberAccess:
				{
					var meme = (MemberExpression)expr;
					var obj = meme.Expression;
					
					if (obj.NodeType == ExpressionType.Parameter) {
						return "\"" + meme.Member.Name + "\"";
					}
					else {
						return CompileParam (Eval (expr));
					}
				}
				default:
					throw new NotSupportedException ();
				}
			}
			
			object Eval (Expression expression)
			{
				return Expression.Lambda<Func<object>>(expression).Compile () ();
			}
		}
		
		class CollectionQueryProvider<U> : IQueryProvider
		{
			Collection<U> _collection;
			
			public CollectionQueryProvider (Collection<U> collection)
			{
				_collection = collection;
			}
			
			public IQueryable CreateQuery (Expression expression)
			{
				throw new NotImplementedException ();
			}
			
			public IQueryable<T> CreateQuery<T> (Expression expression)
			{
				if (expression.NodeType == ExpressionType.Call) {
					var call = (MethodCallExpression)expression;
					var newColl = _collection.Clone<T> (expression);
					switch (call.Method.Name) {
					case "Where":
						newColl.ApplyWhere (((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand).Body);
						break;
					case "OrderByDescending":
						newColl.ApplyOrderByDescending (((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand).Body);
						break;
					case "Take":
						newColl.ApplyTake (call.Arguments [1]);
						break;
					default:
						throw new NotSupportedException (call.Method.Name);
					}
					return newColl;
				}
				else {
					throw new NotSupportedException ();
				}
			}
			
			public object Execute (Expression expression)
			{
				throw new NotImplementedException ();
			}
			
			public T Execute<T> (Expression expression)
			{
				throw new NotImplementedException ();
			}
		}
		
		class TableInfos
		{
			readonly object _tableInfosLock = new object ();
			readonly Dictionary<string, TableInfo> _tableInfos = new Dictionary<string, TableInfo> ();
			
			public TableInfo GetTableInfo (Type type, DataRepository repo)
			{
				var key = type.Name;
				var tableInfo = default (TableInfo);
				lock (_tableInfosLock) {
					if (!_tableInfos.TryGetValue (key, out tableInfo)) {
						tableInfo = new TableInfo (type, repo);
						_tableInfos.Add (key, tableInfo);
					}
				}
				return tableInfo;
			}
		}
		
		static readonly object _tableInfosLock = new object ();
		static readonly Dictionary<string, TableInfos> _tableInfos = new Dictionary<string, TableInfos> ();
		
		TableInfo GetTableInfo (Type type)
		{
			var tableInfos = default(TableInfos);
			var cs = _connection.ConnectionString;
			lock (_tableInfosLock) {
				if (!_tableInfos.TryGetValue (cs, out tableInfos)) {
					tableInfos = new TableInfos ();
					_tableInfos.Add (cs, tableInfos);
				}
			}			
			return tableInfos.GetTableInfo (type, this);
		}
		
		DatabaseInfo _dbInfo = null;
		
		public DatabaseInfo DatabaseInfo
		{
			get {
				if (_dbInfo == null) {
					switch (_connection.GetType ().Name) {
					case "SqliteConnection":
						_dbInfo = new SqliteDatabaseInfo ();
						break;
					default:
						throw new NotSupportedException (_connection.GetType ().Name);
					}
				}
				return _dbInfo;
			}
		}
				
		public override Task<QueryResult> Query (Query query)
		{
			return Task.Factory.StartNew (delegate {
				lock (_connectionLock) {
					var e = query.Perform (_collections);
					return new QueryResult (e);
				}
			});
		}
	}
	
	public class TableInfo
	{
		public readonly string Name;
		
		readonly Dictionary<string, ColumnInfo> _columns = new Dictionary<string, ColumnInfo>();
		
		public TableInfo (Type type, DataRepository repo)
		{
			Name = type.Name;
			
			LearnAboutType (type);
			MigrateDatabase (repo);
		}
		
		void LearnAboutType (Type type)
		{
			foreach (var p in type.GetProperties (System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
				if (!p.CanWrite) return;
				if (p.GetIndexParameters ().Length != 0) return;
				_columns [p.Name] = new ColumnInfo (p);
			}
		}
		
		void MigrateDatabase (DataRepository repo)
		{
			var dbInfo = repo.DatabaseInfo;
			var cols = repo.DatabaseInfo.GetColumnsInTable (Name, repo.Connection);
			
			var commands = new List<string> ();
			
			if (cols.Count == 0) {
				var sb = new StringBuilder ();
				sb.AppendFormat ("create table \"{0}\"(", Name);
				
				var head = "";
				foreach (var c in _columns.Values) {
					sb.Append (head);
					sb.Append (dbInfo.GetColumnDeclaration (c));
					head = ",";
				}
				
				sb.Append (")");
				commands.Add (sb.ToString ());
			}
			else {
				var existingCols = cols.ToDictionary (x => x);
				foreach (var c in _columns.Values) {
					if (!existingCols.ContainsKey (c.Name)) {
						commands.Add ("alter table \"" + Name + "\" add column " + 
						              dbInfo.GetColumnDeclaration (c));
					}
				}
			}
			
			foreach (var commandText in commands) {
				using (var cmd = repo.Connection.CreateCommand ()) {
					cmd.CommandText = commandText;
					cmd.ExecuteNonQuery ();
				}
			}
		}
		
		public ColumnInfo GetColumn (string name)
		{
			return _columns [name];
		}
	}
	
	public class ColumnInfo
	{
		public readonly System.Reflection.PropertyInfo Property;
		public readonly string Name;
		public readonly Type ClrType;
		
		public ColumnInfo (System.Reflection.PropertyInfo property)
		{
			Property = property;
			Name = property.Name;
			ClrType = property.PropertyType;
		}
	}
	
	public abstract class DatabaseInfo
	{
		public abstract string GetColumnDeclaration (ColumnInfo column);
		
		public abstract List<string> GetColumnsInTable (string tableName, IDbConnection connection);
	}
	
	class SqliteDatabaseInfo : DatabaseInfo
	{
		public override string GetColumnDeclaration (ColumnInfo column)
		{
			var sb = new StringBuilder ();
			
			sb.Append ("\"");
			sb.Append (column.Name);
			sb.Append ("\"");
			
			if (column.ClrType == typeof(string)) {
				sb.Append (" text");
			}
			else if (column.ClrType == typeof(int) || column.ClrType == typeof(long)) {
				sb.Append (" integer");
			}
			else if (column.ClrType == typeof(float) || column.ClrType == typeof(double)) {
				sb.Append (" real");
			}
			else if (column.ClrType == typeof(bool)) {
				sb.Append (" boolean");
			}
			else if (column.ClrType == typeof(DateTime)) {
				sb.Append (" datetime");
			}
			else if (column.ClrType == typeof(byte[])) {
				sb.Append (" blob");
			}
			else if (column.ClrType == typeof(decimal)) {
				sb.Append (" decimal");
			}
			else {
				throw new NotSupportedException ("Columns of type " + column.ClrType);
			}
			
			return sb.ToString ();
		}
		
		public override List<string> GetColumnsInTable (string tableName, IDbConnection connection)
		{
			List<string> r = new List<string>();
			
			var query = "pragma table_info(\"" + tableName + "\")";
			
			using (var cmd = connection.CreateCommand ()) {
				cmd.CommandText = query;
				
				var nameIndex = -1;
				
				using (var reader = cmd.ExecuteReader ()) {
					while (reader.Read ()) {
						if (nameIndex < 0) {
							nameIndex = reader.GetOrdinal ("name");
						}
						r.Add (reader.GetString (nameIndex));
					}
				}
			}
			
			return r;
		}
	}
}

