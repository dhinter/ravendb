﻿/// <reference path="../../../../typings/tsd.d.ts"/>

import ongoingTask = require("models/database/tasks/ongoingTaskModel"); 

class ongoingTaskBackupModel extends ongoingTask {

    backupType = ko.observable<any>();
    backupDestinations = ko.observableArray<string>();

    constructor(dto: Raven.Server.Web.System.OngoingTaskBackup) {
        super();
        this.initializeObservables();
        this.update(dto);
    }

    initializeObservables() {
        super.initializeObservables();
        // ...
    }

    update(dto: Raven.Server.Web.System.OngoingTaskBackup) {
        super.update(dto);
        this.backupType(dto.BackupType);
        this.backupDestinations(dto.BackupDestinations);
    }

    enableTask() {
        alert("enabling task backup");
        // ...
    }

    disableTask() {
        alert("disabling task backup");
        // ...
    }

    editTask() {
        alert("edit task backup");
        // ...
    }

    removeTask() {
        alert("remove task backup");
        // ...
    }
}

export = ongoingTaskBackupModel;