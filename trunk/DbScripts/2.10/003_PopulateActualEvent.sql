--//@DEPENDSON = 2.11.2
INSERT INTO ActualEvent(id, name, eventTypeId) VALUES (1, 'Niklas bröt benet', 1)

--//@UNDO

DELETE FROM ActualEvent WHERE id IN (1)