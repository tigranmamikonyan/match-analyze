import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatchService } from '../../services/match.service';
import { RouterModule } from '@angular/router';

@Component({
    selector: 'app-admin',
    standalone: true,
    imports: [CommonModule, RouterModule],
    templateUrl: './admin.component.html',
    styleUrls: ['./admin.component.scss']
})
export class AdminComponent {
    isSyncing = false;

    constructor(private matchService: MatchService) { }

    sync(): void {
        this.isSyncing = true;
        this.matchService.syncMatches(1).subscribe({
            next: (count) => {
                alert(`Synced ${count} new matches/details.`);
                this.isSyncing = false;
            },
            error: (err) => {
                console.error(err);
                alert('Sync failed');
                this.isSyncing = false;
            }
        });
    }

    syncUnparsed(): void {
        this.isSyncing = true;
        this.matchService.syncUnparsedMatches().subscribe({
            next: (count) => {
                alert(`Parsed ${count} historic matches.`);
                this.isSyncing = false;
            },
            error: (err) => {
                console.error(err);
                alert('Sync Unparsed failed');
                this.isSyncing = false;
            }
        });
    }
}
