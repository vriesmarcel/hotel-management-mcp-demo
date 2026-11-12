-- Additive Demo 03 migration. Run while older direct-SQL demo writers are stopped.
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.HotelBookingLifecycle',N'U') IS NULL
  CREATE TABLE dbo.HotelBookingLifecycle(BId int NOT NULL PRIMARY KEY REFERENCES dbo.Booking(BId) ON DELETE CASCADE,
   Status nvarchar(20) NOT NULL CHECK(Status IN(N'Reserved',N'CheckedIn',N'CheckedOut')));
 INSERT dbo.HotelBookingLifecycle(BId,Status)
 SELECT b.BId,CASE WHEN EXISTS(SELECT 1 FROM dbo.Room r WHERE r.BId=b.BId AND r.RId=b.RId AND r.IsBooked=N'Yes') THEN N'CheckedIn'
  WHEN b.CheckOut<=CONVERT(date,GETDATE()) AND b.Remaining=0 THEN N'CheckedOut' ELSE N'Reserved' END
 FROM dbo.Booking b WHERE NOT EXISTS(SELECT 1 FROM dbo.HotelBookingLifecycle s WHERE s.BId=b.BId);
 IF OBJECT_ID(N'dbo.HotelQuotes',N'U') IS NULL
  CREATE TABLE dbo.HotelQuotes(QuoteId uniqueidentifier NOT NULL PRIMARY KEY,RId int NOT NULL,FId int NOT NULL,SId int NOT NULL,
   CheckIn date NOT NULL,CheckOut date NOT NULL,Total decimal(18,2) NOT NULL CHECK(Total>=0),ExpiresUtc datetime2 NOT NULL);
 IF OBJECT_ID(N'dbo.HotelOperations',N'U') IS NULL
  CREATE TABLE dbo.HotelOperations(OperationId nvarchar(100) NOT NULL PRIMARY KEY,PayloadHash char(64) NOT NULL,
   BId int NOT NULL,Amount decimal(18,2) NOT NULL CHECK(Amount>=0));
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
