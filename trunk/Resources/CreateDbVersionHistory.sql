CREATE TABLE [dbo].[DbVersioningHistory](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[ScriptVersion] [nvarchar](100) NOT NULL,
	[ExecutedFrom] [nvarchar](512) NOT NULL,
	[Description] [nvarchar](512) NOT NULL,
	[DependentOnScriptVersion] [nvarchar](100) NULL,
	[DateExecutedUtc] [datetime] NOT NULL,
	[Signature] [nvarchar](512) NOT NULL
 CONSTRAINT [PK_DbVersioningHistory] PRIMARY KEY CLUSTERED 
(
	[ScriptVersion] ASC
)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
) ON [PRIMARY]

GO

ALTER TABLE [dbo].[DbVersioningHistory] CHECK CONSTRAINT [FK_DbVersioningHistory_DbVersioningHistory]
GO