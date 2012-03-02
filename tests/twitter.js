{
  schema: {
    User: {
      screenName: String,
    },
    UserFriend: {
      user: "User",
      friend: "User",
    },
    Tweet: {
      user: "User",
      text: "String",
      postedDate: Date,
    }
  }
  
  resources: [
  
    "statuses/home_timeline":
    {
      description: "Returns...",
      authenticate: true,

      parameters: {
        "count": {
          type: Number,
          required: false,
          description: "Specifies the number of records to retrieve.",
          max: 200,
        }
      }
      
      get: "
        var friends = select friend from UserFriend where user = $user;
        var tweets = select * from Tweet 
                     where user in friends or user == $user
                     orderby postedDate;
        if (:count) {
          tweets = tweets.trim(:count);
        }
        return tweets;
      "
    },
    
    "followers/ids": {
      authenticate: true,
      get: "
        return select friend.id from UserFriend where user = $user;
      "
    },
    
    
  ]
}
