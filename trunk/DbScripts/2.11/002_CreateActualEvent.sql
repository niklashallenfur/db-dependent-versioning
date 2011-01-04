CREATE TABLE [dbo].[ActualEvent](
	[id] [int] NOT NULL,
	[name] [nvarchar](50) NOT NULL,
	[eventTypeId] [int] NOT NULL,
 CONSTRAINT [PK_ActualEvent] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
	
	
--//@UNDO

DROP TABLE ActualEvent