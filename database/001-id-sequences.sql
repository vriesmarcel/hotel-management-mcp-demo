-- Apply explicitly to the intended database while baseline writers are stopped.
-- Adds objects only: no existing rows, column types, constraints or IDs are changed.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @next bigint, @sql nvarchar(200);

    IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.HotelRoomId'))
    BEGIN
        SELECT @next = COALESCE(MAX(CONVERT(bigint,RId)),0)+1 FROM dbo.Room WITH (TABLOCKX,HOLDLOCK);
        IF @next > 2147483647 THROW 50001, 'Room ID range is exhausted.', 1;
        SET @sql = N'CREATE SEQUENCE dbo.HotelRoomId AS int START WITH ' + CONVERT(nvarchar(20),@next) + N' INCREMENT BY 1 NO CYCLE;';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.HotelFoodPackageId'))
    BEGIN
        SELECT @next = COALESCE(MAX(CONVERT(bigint,FId)),0)+1 FROM dbo.FoodPackage WITH (TABLOCKX,HOLDLOCK);
        IF @next > 2147483647 THROW 50002, 'Food package ID range is exhausted.', 1;
        SET @sql = N'CREATE SEQUENCE dbo.HotelFoodPackageId AS int START WITH ' + CONVERT(nvarchar(20),@next) + N' INCREMENT BY 1 NO CYCLE;';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.HotelServicePackageId'))
    BEGIN
        SELECT @next = COALESCE(MAX(CONVERT(bigint,SId)),0)+1 FROM dbo.ServicePackage WITH (TABLOCKX,HOLDLOCK);
        IF @next > 2147483647 THROW 50003, 'Service package ID range is exhausted.', 1;
        SET @sql = N'CREATE SEQUENCE dbo.HotelServicePackageId AS int START WITH ' + CONVERT(nvarchar(20),@next) + N' INCREMENT BY 1 NO CYCLE;';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.HotelBookingId'))
    BEGIN
        SELECT @next = COALESCE(MAX(CONVERT(bigint,BId)),0)+1 FROM dbo.Booking WITH (TABLOCKX,HOLDLOCK);
        IF @next > 2147483647 THROW 50004, 'Booking ID range is exhausted.', 1;
        SET @sql = N'CREATE SEQUENCE dbo.HotelBookingId AS int START WITH ' + CONVERT(nvarchar(20),@next) + N' INCREMENT BY 1 NO CYCLE;';
        EXEC sys.sp_executesql @sql;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
