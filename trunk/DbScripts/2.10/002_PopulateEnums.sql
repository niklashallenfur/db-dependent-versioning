INSERT INTO EventType VALUES (1, "Snubblade")
INSERT INTO EventType VALUES (2, "Blev rånad")
INSERT INTO EventType VALUES (3, "Åt middag")
INSERT INTO EventType VALUES (4, "Krockade bilen")
INSERT INTO EventType VALUES (5, "Bensinstopp")

--//@UNDO

DELETE FROM EventType WHERE id IN (1,2,3,4,5)
