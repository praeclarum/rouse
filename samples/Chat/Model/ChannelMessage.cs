using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Rouse;

namespace Chat
{
	public class Message : Resource
	{
		[PrimaryKey]
		public string Id { get; set; }
		[Required]
		public string Username { get; set; }
		[Required]
		public string Text { get; set; }
		[Required]
		public string ChannelName { get; set; }
		public DateTime PostTime { get; set; }
		
		public Message ()
		{
			Username = "";
			Text = "";
			ChannelName = "";
		}
	}
	
	public class Channel : Resource
	{
		[PrimaryKey]
		public string Name { get; set; }
	}
	
	public class Channels : Query
	{
		public override System.Collections.IEnumerable Get (ICollectionFactory collections)
		{
			return collections.Get<Channel> ();
		}
	}
	
	public class RecentChannelMessages : Query
	{
		public string Channel { get; set; }
		
		public RecentChannelMessages ()
		{
			Channel = "";
		}
		
		public RecentChannelMessages (string channel)
		{
			if (string.IsNullOrEmpty (channel)) throw new ArgumentNullException ("channel");
			Channel = channel;
		}
		
		public override System.Collections.IEnumerable Get (ICollectionFactory collections)
		{
			var q = from m in collections.Get<Message> ()
					where m.ChannelName == Channel
					orderby m.PostTime descending
					select m;
			return q.Take (20);
		}
	}
}

