using System;
using System.Linq;
using Rouse;

namespace Chat
{
	public class ChannelMessage
	{
		public string Username { get; set; }
		public string Text { get; set; }
		public string Channel { get; set; }
		public DateTime PostTime { get; set; }
		
		public ChannelMessage ()
		{
			Username = "";
			Text = "";
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
		
		public override System.Collections.IEnumerable Perform (ICollectionFactory collections)
		{
			var q = from m in collections.Get<ChannelMessage> ()
					where m.Channel == Channel
					orderby m.PostTime descending
					select m;
			return q.Take (20);
		}
	}	
}

