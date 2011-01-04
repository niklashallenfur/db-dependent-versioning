INSERT INTO EventType(id, name) VALUES (1, 'Snubblade')
INSERT INTO EventType(id, name) VALUES (2, 'Blev rånad')
INSERT INTO EventType(id, name) VALUES (3, 'Åt middag')
INSERT INTO EventType(id, name) VALUES (4, 'Krockade bilen')
INSERT INTO EventType(id, name) VALUES (5, 'Bensinstopp')

--//@UNDO

DELETE FROM EventType WHERE id IN (1,2,3,4,5)
