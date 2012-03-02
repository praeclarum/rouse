{
  schema: {
    "Comment": {
      title: String,
      body: String,
    },
    
    "Entry": {
      title: String,
      bodyHtml: String,
      publishDate: Date,
    }
  }
  
  resources: [
    {
      name: "entries",
      query: "select * from Comment where "
    }
  ]
}

