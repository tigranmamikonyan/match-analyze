import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatchService } from '../../services/match.service';
import { Match } from '../../models/match';
import { MatchCardComponent } from '../../components/match-card/match-card.component';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatchCardComponent, FormsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  matches: Match[] = [];
  isLoading: boolean = false;
  isSyncing = false;

  // Filter State
  filter = {
    from: new Date().toISOString().split('T')[0],
    to: null,
    team: '',
    conditions: [
      { enabled: true, type: 'over', threshold: 'o05', half: 'full', minPercent: 60, maxPercent: 100 },
      { enabled: false, type: 'over', threshold: 'o05', half: 'full', minPercent: 0, maxPercent: 100 },
      { enabled: false, type: 'over', threshold: 'o05', half: 'full', minPercent: 0, maxPercent: 100 }
    ]
  };

  isFiltersVisible = true;

  constructor(private matchService: MatchService) {
    this.loadMatches();
  }

  ngOnInit(): void {
    // ngOnInit is now empty as loadMatches is called in constructor
  }

  toggleFilters() {
    this.isFiltersVisible = !this.isFiltersVisible;
  }

  loadMatches(): void {
    this.isLoading = true;

    // Construct request
    const request = {
      from: this.filter.from,
      to: this.filter.to,
      team: this.filter.team,
      conditions: this.filter.conditions
    };

    console.log('Searching matches:', request);

    this.matchService.searchMatches(request).subscribe({
      next: (data) => {
        this.matches = data;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Failed to load matches', err);
        this.isLoading = false;
      }
    });
  }

  sync(): void {
    this.isSyncing = true;
    this.matchService.syncMatches(1).subscribe({
      next: (count) => {
        alert(`Synced ${count} new matches/details.`);
        this.isSyncing = false;
        this.loadMatches();
      },
      error: (err) => {
        console.error(err);
        alert('Sync failed');
        this.isSyncing = false;
      }
    });
  }
}
