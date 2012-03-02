using System;
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
	
	public class RecentChannelMessages : Query<ChannelMessage>
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
		
		public override CollectionQuery<ChannelMessage> Filter (CollectionQuery<ChannelMessage> source)
		{
			var q = from m in source
					where m.Channel == Channel
					orderby m.PostTime descending
					select m;
			return q.Take (20);
		}
	}	
}

