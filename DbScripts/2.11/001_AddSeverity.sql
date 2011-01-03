ALTER TABLE EventType ADD COLUMN Severity varchar(50) 

--//@UNDO

DELETE FROM EventType WHERE id IN (1,2,3,4,5)
