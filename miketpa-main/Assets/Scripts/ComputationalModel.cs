
using System.Diagnostics;


namespace Assets.Scripts
{
    public class ComputationalModel
    {
        //Vous pouvez déclarer tout un ensemble de variables utiles ici
        private int nombreDeTours = 5;


        //Vous pouvez déclarer un constructeur avec paramètre si vous voulez 
        public ComputationalModel() { 
            nombreDeTours = 5;
        }
        public ComputationalModel(int nbTours)
        {
            nombreDeTours = nbTours;
        }

        public void UserValues(string values)
        {
            nombreDeTours++;
        }

        public void LLMValues(string values)
        {
            nombreDeTours++;
        }

        //Ici je simule que l'émotion du système est égale au nombre de tours de parole.
        //Pour l'exemple, je m'en sers pour demander au LLM de répondre de manière de plus en plus aggressive (il perd patience)
        public int getEmotion()
        {
            return nombreDeTours;
        }
    }
}
