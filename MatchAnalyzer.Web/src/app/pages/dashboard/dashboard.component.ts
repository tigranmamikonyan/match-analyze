import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatchService } from '../../services/match.service';
import { Match } from '../../models/match';
import { MatchCardComponent, ThresholdConfig } from '../../components/match-card/match-card.component';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatchCardComponent, FormsModule, RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  matches: Match[] = [];
  isLoading: boolean = false;


  // Filter State
  filter = {
    from: new Date().toISOString().split('T')[0],
    to: null,
    team: '',
    conditions: [
      { enabled: false, type: 'over', threshold: 'o05', half: 'full', minPercent: 60, maxPercent: 100 },
      { enabled: false, type: 'over', threshold: 'o05', half: 'full', minPercent: 0, maxPercent: 100 },
      { enabled: false, type: 'over', threshold: 'o05', half: 'full', minPercent: 0, maxPercent: 100 }
    ]
  };

  thresholds: ThresholdConfig = this.loadSettings();

  isFiltersVisible = true;

  constructor(private matchService: MatchService) {
    this.loadMatches();
  }

  private loadSettings(): ThresholdConfig {
    const saved = localStorage.getItem('match_analyzer_thresholds');
    if (saved) {
      try {
        return JSON.parse(saved);
      } catch (e) {
        console.error('Failed to parse settings', e);
      }
    }
    return {
      over05: 90,
      over15: 80,
      over25: 70,
      overFH05: 70,
      overFH15: 60,
      overFH25: 50
    };
  }

  saveSettings(): void {
    localStorage.setItem('match_analyzer_thresholds', JSON.stringify(this.thresholds));
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

}
