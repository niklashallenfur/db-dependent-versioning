ALTER TABLE EventType ADD Severity varchar(50) NOT NULL CONSTRAINT [TEMP_DEFAULT] DEFAULT ('Not Defined')
ALTER TABLE EventType DROP CONSTRAINT [TEMP_DEFAULT]
--//@UNDO

DELETE FROM EventType WHERE id IN (1,2,3,4,5)