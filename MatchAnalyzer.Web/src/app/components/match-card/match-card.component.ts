import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Match } from '../../models/match';
import { MatchAnalysis } from '../../models/match-analysis';
import { MatchService } from '../../services/match.service';

@Component({
  selector: 'app-match-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './match-card.component.html',
  styleUrls: ['./match-card.component.scss']
})
export class MatchCardComponent {
  @Input() match!: Match;

  analysis: MatchAnalysis | null = null;
  isLoadingAnalysis = false;
  isExpanded = false;

  constructor(private matchService: MatchService) { }

  toggleAnalysis() {
    this.isExpanded = !this.isExpanded;
    if (this.isExpanded && !this.analysis) {
      this.isLoadingAnalysis = true;
      this.matchService.getAnalysis(this.match.id).subscribe({
        next: (data) => {
          this.analysis = data;
          this.isLoadingAnalysis = false;
        },
        error: (err) => {
          console.error(err);
          this.isLoadingAnalysis = false;
        }
      });
    }
  }

  get isLive(): boolean {
    return false;
  }
}
