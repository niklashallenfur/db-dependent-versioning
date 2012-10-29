IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_DbVersioningHistory_DbVersioningHistory]') AND parent_object_id = OBJECT_ID(N'[dbo].[DbVersioningHistory]'))
ALTER TABLE [dbo].[DbVersioningHistory] DROP CONSTRAINT [FK_DbVersioningHistory_DbVersioningHistory]
GO